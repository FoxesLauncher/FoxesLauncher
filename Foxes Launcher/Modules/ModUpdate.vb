Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates

''' <summary>
''' 启动器更新：按区域选择源、验签 manifest、防回滚、下载校验与更新器入口。
''' 所有源地址与公钥均来自 .env（由 GeneratedSecrets.vb 注入）。
''' </summary>
Friend Module ModUpdate

    Friend Const UpdateProductName As String = "foxes-launcher"

#Region "数据模型"

    Friend Class UpdateAsset
        Public Name As String
        Public Size As Long
        Public Sha256 As String
        Public Urls As New List(Of String)
    End Class

    Friend Class UpdateInfo
        Public Schema As Integer
        Public Channel As String
        Public Platform As String
        Public VersionCode As Integer
        Public VersionName As String
        Public MinVersionCode As Integer
        Public ReleaseId As String
        Public PublishedAt As Date?
        Public NotesUrl As String
        Public Assets As New List(Of UpdateAsset)
        Public SourceName As String
        Public MustUpdate As Boolean
    End Class

    Friend Class UpdateCheckResult
        Public Success As Boolean
        Public Info As UpdateInfo
        Public SourceName As String
        Public ErrorMessage As String
        Public ElapsedMs As Long
        Public FromCache As Boolean
    End Class

    Private Class UpdateSourceEntry
        Public Name As String
        Public Url As String
    End Class

#End Region

#Region "源选择"

    Friend Function GetUpdateChannel() As String
        Dim Value = Settings.Get(Of String)("UpdateChannel")
        If Value = "official" OrElse Value = "snapshot" OrElse Value = "open-source" Then Return Value
        If Not String.IsNullOrWhiteSpace(UpdateChannel) Then Return UpdateChannel
        Return "official"
    End Function

    Private Function IsChinaRegion() As Boolean
        Return CurrentRegion = RegionAsiaMainlandChina OrElse CurrentRegion = RegionAsiaHongKongMacaoTaiwan
    End Function

    Private Function BuildUpdateSources() As List(Of UpdateSourceEntry)
        Dim Channel = GetUpdateChannel()
        Dim Platform = If(String.IsNullOrWhiteSpace(UpdatePlatform), "win-x64", UpdatePlatform)
        Dim R2Url = UpdateR2Latest.Replace("{channel}", Channel).Replace("{platform}", Platform)

        Dim All As New List(Of UpdateSourceEntry) From {
            New UpdateSourceEntry With {.Name = "gitcode", .Url = UpdateGitCodeLatest},
            New UpdateSourceEntry With {.Name = "github", .Url = UpdateGitHubLatest},
            New UpdateSourceEntry With {.Name = "cloudflare", .Url = R2Url}
        }
        All.RemoveAll(Function(s) String.IsNullOrWhiteSpace(s.Url))

        '按区域确定默认顺序
        Dim PreferredOrder As String() = If(IsChinaRegion(),
            {"gitcode", "github", "cloudflare"},
            {"github", "gitcode", "cloudflare"})
        Dim Result As New List(Of UpdateSourceEntry)
        For Each Name In PreferredOrder
            Result.AddRange(All.Where(Function(s) s.Name = Name))
        Next

        '用户在设置中强制指定源时，把该源提前
        Dim Preferred = Settings.Get(Of String)("UpdateSource")
        If Not String.IsNullOrWhiteSpace(Preferred) AndAlso Preferred <> "auto" Then
            Dim Match = Result.Where(Function(s) s.Name = Preferred).ToList()
            If Match.Any Then
                Result = Match.Concat(Result.Where(Function(s) s.Name <> Preferred)).ToList()
            End If
        End If
        Return Result
    End Function

#End Region

#Region "检查更新"

    ''' <summary>
    ''' 检查更新。Manual=True 时忽略缓存；自动检查最多缓存 6 小时。
    ''' 单源超时 10 秒，总预算 30 秒。
    ''' </summary>
    Friend Function CheckForUpdate(Optional Manual As Boolean = False, Optional IgnoreCache As Boolean = False) As UpdateCheckResult
        Dim StartTick = GetTimeMs()
        Dim Result As New UpdateCheckResult With {.Success = False}

        If String.IsNullOrWhiteSpace(UpdatePublicKeys) Then
            Result.ErrorMessage = T("当前构建未配置更新公钥，已禁用更新检查")
            Return Result
        End If

        If Not Manual AndAlso Not IgnoreCache Then
            Dim Cached = ReadUpdateCache()
            If Cached IsNot Nothing Then
                Result.Success = True
                Result.Info = Cached
                Result.SourceName = "cache"
                Result.FromCache = True
                Result.ElapsedMs = GetTimeMs() - StartTick
                Return Result
            End If
        End If

        Dim Sources = BuildUpdateSources()
        If Sources.Count = 0 Then
            Result.ErrorMessage = T("未配置任何更新源，请检查 .env 中的 FXL_UPDATE_* 设置")
            ReportUpdateCheckResult(Result)
            Return Result
        End If

        Dim LastError As String = Nothing
        Dim LastSource As String = Nothing
        For Each Source In Sources
            Dim Remaining = 30000 - (GetTimeMs() - StartTick)
            If Remaining <= 1000 Then
                LastError = T("检查更新超时")
                Exit For
            End If
            LastSource = Source.Name
            Dim SourceStart = GetTimeMs()
            Try
                Dim Raw = NetRequestByClient(Source.Url, Timeout:=Math.Min(10000, CInt(Remaining)), MakeLog:=False)
                Dim Document As ModRemoteCrypto.RemoteSignedDocument = Nothing
                Dim VerifyError As String = Nothing
                If Not ModRemoteCrypto.VerifySignedDocument(Raw, Document, VerifyError) Then
                    Throw New Exception(T("manifest 验签失败：{0}", VerifyError))
                End If
                Dim Info = ParseManifest(Document.Payload, Source.Name)
                ValidateAntiRollback(Info)

                Info.SourceName = Source.Name
                Info.MustUpdate = Info.MinVersionCode > VersionCode
                SaveUpdateCache(Raw, Info)
                Settings.Set("UpdateLastCheck", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                If Settings.Get(Of Integer)("UpdateLastSeenCode") < Info.VersionCode Then
                    Settings.Set("UpdateLastSeenCode", Info.VersionCode)
                End If

                Result.Success = True
                Result.Info = Info
                Result.SourceName = Source.Name
                Result.ElapsedMs = GetTimeMs() - StartTick
                ReportUpdateCheckResult(Result, GetTimeMs() - SourceStart)
                Return Result
            Catch ex As Exception
                ex.ThrowIfCanceled()
                LastError = ex.GetDisplay(True)
                Logger.Warn(ex, $"从 {Source.Name} 检查更新失败")
            End Try
        Next

        Result.SourceName = LastSource
        Result.ErrorMessage = If(LastError, T("所有更新源均不可用"))
        Result.ElapsedMs = GetTimeMs() - StartTick
        ReportUpdateCheckResult(Result)
        Return Result
    End Function

    Private Sub ReportUpdateCheckResult(Result As UpdateCheckResult, Optional SourceElapsedMs As Long = -1)
        Try
            If Result.Success Then
                Telemetry("UpdateCheckResult",
                    "Source", Result.SourceName,
                    "Result", "success",
                    "LatestVersionCode", Result.Info.VersionCode.ToString(),
                    "DurationMs", Result.ElapsedMs.ToString(),
                    "FromCache", Result.FromCache.ToString())
            Else
                Telemetry("UpdateCheckResult",
                    "Source", If(Result.SourceName, "none"),
                    "Result", "failure",
                    "DurationMs", Result.ElapsedMs.ToString(),
                    "ExceptionCategory", "update-check")
            End If
        Catch
        End Try
    End Sub

    Private Function ToInt(Token As JToken, Optional DefaultValue As Integer = 0) As Integer
        If Token Is Nothing Then Return DefaultValue
        Try
            Return Token.ToObject(Of Integer)()
        Catch
            Return DefaultValue
        End Try
    End Function

    Private Function ToLong(Token As JToken, Optional DefaultValue As Long = 0) As Long
        If Token Is Nothing Then Return DefaultValue
        Try
            Return Token.ToObject(Of Long)()
        Catch
            Return DefaultValue
        End Try
    End Function

    Private Function ParseManifest(Payload As JObject, SourceName As String) As UpdateInfo
        If Payload Is Nothing Then Throw New FormatException(T("manifest 为空"))
        If ToInt(Payload("schema")) <> 1 Then Throw New FormatException(T("不支持的 manifest schema"))
        If Not String.Equals(Payload("product")?.ToString, UpdateProductName, StringComparison.OrdinalIgnoreCase) Then
            Throw New FormatException(T("manifest 产品标识不匹配"))
        End If

        Dim Channel = GetUpdateChannel()
        If Not String.Equals(Payload("channel")?.ToString, Channel, StringComparison.OrdinalIgnoreCase) Then
            Throw New FormatException(T("manifest 通道不匹配（期望 {0}）", Channel))
        End If
        Dim Platform = If(String.IsNullOrWhiteSpace(UpdatePlatform), "win-x64", UpdatePlatform)
        If Not String.Equals(Payload("platform")?.ToString, Platform, StringComparison.OrdinalIgnoreCase) Then
            Throw New FormatException(T("manifest 平台不匹配（期望 {0}）", Platform))
        End If

        Dim Info As New UpdateInfo With {
            .Schema = 1,
            .Channel = Channel,
            .Platform = Platform,
            .SourceName = SourceName
        }
        Info.VersionCode = ToInt(Payload("versionCode"))
        If Info.VersionCode <= 0 Then Throw New FormatException(T("manifest 版本号无效"))
        Info.VersionName = Payload("versionName")?.ToString
        Info.MinVersionCode = ToInt(Payload("minVersionCode"))
        Info.ReleaseId = Payload("releaseId")?.ToString
        Info.NotesUrl = Payload("notesUrl")?.ToString
        If Not String.IsNullOrWhiteSpace(Info.NotesUrl) AndAlso Not Info.NotesUrl.StartsWithF("https://", True) Then Info.NotesUrl = Nothing

        Dim PublishedText = Payload("publishedAt")?.ToString
        Dim Published As Date = Date.MinValue
        If Not String.IsNullOrWhiteSpace(PublishedText) AndAlso
           Date.TryParse(PublishedText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, Published) Then
            Info.PublishedAt = Published
        End If

        Dim Assets = TryCast(Payload("assets"), JArray)
        If Assets Is Nothing OrElse Assets.Count = 0 Then Throw New FormatException(T("manifest 未包含安装包"))
        For Each Token In Assets
            Dim AssetToken = TryCast(Token, JObject)
            If AssetToken Is Nothing Then Continue For
            Dim Asset As New UpdateAsset With {
                .Name = AssetToken("name")?.ToString,
                .Size = ToLong(AssetToken("size"), -1),
                .Sha256 = AssetToken("sha256")?.ToString
            }
            If String.IsNullOrWhiteSpace(Asset.Name) OrElse String.IsNullOrWhiteSpace(Asset.Sha256) Then
                Throw New FormatException(T("manifest 安装包信息不完整"))
            End If
            If Asset.Sha256.Trim().Length <> 64 Then Throw New FormatException(T("manifest 安装包 SHA-256 格式不正确"))
            Dim Urls = TryCast(AssetToken("urls"), JArray)
            If Urls Is Nothing OrElse Urls.Count = 0 Then Throw New FormatException(T("manifest 安装包缺少下载地址"))
            For Each UrlToken In Urls
                Dim Url = UrlToken?.ToString
                If Url IsNot Nothing AndAlso (Url.StartsWithF("https://", True) OrElse Url.StartsWithF("http://", True)) Then
                    Asset.Urls.Add(Url)
                End If
            Next
            If Asset.Urls.Count = 0 Then Throw New FormatException(T("manifest 安装包没有可用的 HTTPS 地址"))
            Info.Assets.Add(Asset)
        Next
        Return Info
    End Function

    Private Sub ValidateAntiRollback(Info As UpdateInfo)
        If Info.VersionCode < VersionCode Then
            Throw New FormatException(T("拒绝版本回退：manifest 版本 {0} 低于当前版本 {1}", Info.VersionCode, VersionCode))
        End If
        Dim LastSeen = Settings.Get(Of Integer)("UpdateLastSeenCode")
        If LastSeen > 0 AndAlso Info.VersionCode < LastSeen Then
            Throw New FormatException(T("拒绝版本回退：manifest 版本 {0} 低于已见版本 {1}", Info.VersionCode, LastSeen))
        End If
    End Sub

    Private Function UpdateCacheFolder() As String
        Return PathTemp & "Cache\Update\"
    End Function

    Private Function UpdateCacheFile() As String
        Return UpdateCacheFolder() & "latest.json"
    End Function

    Private Function UpdateCacheTimeFile() As String
        Return UpdateCacheFolder() & "latest.time"
    End Function

    ''' <summary>
    ''' 读取 6 小时内的缓存 manifest。缓存同样需要验签，避免本地文件被替换。
    ''' </summary>
    Friend Function ReadUpdateCache() As UpdateInfo
        Try
            Dim CacheFile = UpdateCacheFile()
            Dim TimeFile = UpdateCacheTimeFile()
            If Not FileUtils.Exists(CacheFile) OrElse Not FileUtils.Exists(TimeFile) Then Return Nothing
            Dim CachedAt As Long
            If Not Long.TryParse(FileUtils.ReadAsString(TimeFile).Trim(), CachedAt) Then Return Nothing
            If DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - CachedAt > 6 * 60 * 60 * 1000 Then Return Nothing

            Dim Raw = FileUtils.ReadAsString(CacheFile)
            Dim Document As ModRemoteCrypto.RemoteSignedDocument = Nothing
            Dim VerifyError As String = Nothing
            If Not ModRemoteCrypto.VerifySignedDocument(Raw, Document, VerifyError) Then
                Logger.Warn($"更新缓存验签失败：{VerifyError}")
                Return Nothing
            End If
            Dim Info = ParseManifest(Document.Payload, "cache")
            ValidateAntiRollback(Info)
            Info.MustUpdate = Info.MinVersionCode > VersionCode
            Return Info
        Catch ex As Exception
            Logger.Warn(ex, "读取更新缓存失败")
            Return Nothing
        End Try
    End Function

    Private Sub SaveUpdateCache(Raw As String, Info As UpdateInfo)
        Try
            DirectoryUtils.Create(UpdateCacheFolder())
            FileUtils.Write(UpdateCacheFile(), Raw)
            FileUtils.Write(UpdateCacheTimeFile(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
        Catch ex As Exception
            Logger.Warn(ex, "写入更新缓存失败")
        End Try
    End Sub

    ''' <summary>
    ''' 供设置页等位置判断缓存中的最新版本。没有有效缓存时返回 Nothing。
    ''' </summary>
    Friend Function TryGetCachedUpdate() As UpdateInfo
        Return ReadUpdateCache()
    End Function

    ''' <summary>
    ''' 启动后后台检查更新。失败只写日志，不弹窗。
    ''' </summary>
    Friend Sub StartAutomaticCheck()
        If BuildType = BuildTypes.Debug Then Return
        If Not Settings.Get(Of Boolean)("UpdateAutoCheck") Then Return
        RunInNewThread(
        Sub()
            Try
                Thread.Sleep(8000)
                Dim Result = CheckForUpdate(False)
                If Result.Success AndAlso Result.Info IsNot Nothing AndAlso Result.Info.VersionCode > VersionCode Then
                    If Settings.Get(Of Integer)("UpdatePostponedCode") <> Result.Info.VersionCode Then
                        RunInUi(Sub() ShowUpdateCheckResult(Result, IsAutomatic:=True))
                    End If
                End If
                If Result.Success Then CheckTlsSoftPinIfNeeded()
            Catch ex As Exception
                Logger.Warn(ex, "后台检查更新失败")
            End Try
        End Sub, "Update Auto Check", ThreadPriority.BelowNormal)
    End Sub

    ''' <summary>
    ''' 手动检查更新（设置页按钮）。
    ''' </summary>
    Friend Sub UpdateCheckByButton()
        If BuildType = BuildTypes.Debug Then
            Hint(T("该版本中不包含更新功能……"))
            Return
        End If
        If Interlocked.CompareExchange(CheckingUpdate, 1, 0) <> 0 Then Return
        RunInNewThread(
        Sub()
            Try
                RunInUi(Sub() Hint(T("正在检查更新……"), HintType.Blue, False))
                Dim Result = CheckForUpdate(True)
                RunInUi(Sub() ShowUpdateCheckResult(Result))
            Catch ex As Exception
                Logger.Error(ex, "检查更新失败")
                RunInUi(Sub() MyMsgBox(T("检查更新失败：{0}", ex.Message), T("检查更新"), IsWarn:=True))
            Finally
                Interlocked.Exchange(CheckingUpdate, 0)
            End Try
        End Sub, "Update Check Button", ThreadPriority.BelowNormal)
    End Sub

    Private CheckingUpdate As Integer

    Private Sub ShowUpdateCheckResult(Result As UpdateCheckResult, Optional IsAutomatic As Boolean = False)
        If Not Result.Success Then
            Dim CheckTitle = T("检查更新")
            MyMsgBox(T("检查更新失败。") & vbCrLf & vbCrLf & Result.ErrorMessage, CheckTitle, IsWarn:=True)
            Return
        End If

        Dim Info = Result.Info
        If Info Is Nothing OrElse Info.VersionCode <= VersionCode Then
            Dim Suffix = If(Result.FromCache, T("（来自 {0} 的本地缓存）", Result.SourceName), T("（来源：{0}）", Result.SourceName))
            Dim NewestText = T("当前已是最新版本。")
            Dim CurrentText = T("当前版本：{0} ({1})", VersionBaseName, VersionCode)
            Dim CheckTitle = T("检查更新")
            MyMsgBox(NewestText & vbCrLf & vbCrLf & CurrentText & vbCrLf & Suffix, CheckTitle)
            Return
        End If

        Dim AssetSize = If(Info.Assets(0).Size > 0, FormatSize(Info.Assets(0).Size), T("未知"))
        Dim Message = T("发现新版本！") & vbCrLf & vbCrLf &
            T("当前版本：{0} ({1})", VersionBaseName, VersionCode) & vbCrLf &
            T("最新版本：{0} ({1})", Info.VersionName, Info.VersionCode) & vbCrLf &
            T("安装包大小：{0}", AssetSize) & vbCrLf &
            T("更新来源：{0}", Result.SourceName)
        If Info.MustUpdate Then
            Dim MustText = T("此版本要求至少更新到 {0}。", Info.MinVersionCode)
            Message &= vbCrLf & vbCrLf & MustText
        End If
        If Not String.IsNullOrWhiteSpace(Info.NotesUrl) Then
            Dim NotesText = T("更新日志：{0}", Info.NotesUrl)
            Message &= vbCrLf & NotesText
        End If

        Dim Choice As Integer
        If Not String.IsNullOrWhiteSpace(Info.NotesUrl) Then
            Dim NotesUrl = Info.NotesUrl
            Choice = MyMsgBox(Message, T("发现新版本"), T("下载并更新"), T("暂缓更新"), T("查看更新日志"),
                Button3Action:=Sub() OpenWebsite(NotesUrl))
        Else
            Choice = MyMsgBox(Message, T("发现新版本"), T("下载并更新"), T("暂缓更新"))
        End If

        Select Case Choice
            Case 1
                Settings.Set("UpdatePostponedCode", 0)
                StartUpdateDownload(Info)
            Case 2
                If IsAutomatic Then Settings.Set("UpdatePostponedCode", Info.VersionCode)
                Telemetry("UpdatePostponed", "FromVersionCode", VersionCode.ToString(), "ToVersionCode", Info.VersionCode.ToString())
            Case 3
                Telemetry("NoticeClicked", "Kind", "update-notes")
        End Select
    End Sub

    Private Sub StartUpdateDownload(Info As UpdateInfo)
        RunInNewThread(
        Sub()
            Try
                RunInUi(Sub() Hint(T("正在下载更新包……"), HintType.Blue, False))
                Dim Target = DownloadUpdatePackage(Info)
                RunInUi(
                Sub()
                    Dim Choice = MyMsgBox(T("更新包已下载并通过校验。") & vbCrLf & vbCrLf &
                        T("版本：{0} ({1})", Info.VersionName, Info.VersionCode) & vbCrLf &
                        T("保存位置：{0}", Target) & vbCrLf & vbCrLf &
                        T("是否立即重启并完成更新？"), T("更新已就绪"), T("立即重启"), T("稍后"))
                    If Choice = 1 Then UpdateRestart(True)
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "下载更新包失败")
                Telemetry("UpdateDownloadFailed", "Source", Info.SourceName, "ExceptionCategory", "update-download")
                RunInUi(Sub() MyMsgBox(T("下载更新包失败：{0}", ex.GetDisplay(True)), T("更新失败"), IsWarn:=True))
            End Try
        End Sub, "Update Download", ThreadPriority.BelowNormal)
    End Sub

    Private Function FormatSize(Bytes As Long) As String
        If Bytes >= 1024L * 1024 * 1024 Then Return (Bytes / 1024.0 / 1024 / 1024).ToString("0.0") & " GB"
        If Bytes >= 1024L * 1024 Then Return (Bytes / 1024.0 / 1024).ToString("0.0") & " MB"
        If Bytes >= 1024 Then Return (Bytes / 1024.0).ToString("0.0") & " KB"
        Return Bytes.ToString() & " B"
    End Function

#End Region

#Region "下载与更新器"

    Private PendingUpdateInfo As UpdateInfo
    Private PendingUpdatePath As String

    ''' <summary>
    ''' 下载安装包到临时目录，并校验大小、SHA-256 与可选的 Authenticode。
    ''' </summary>
    Friend Function DownloadUpdatePackage(Info As UpdateInfo, Optional LoaderToSyncProgress As LoaderBase = Nothing) As String
        If Info Is Nothing OrElse Info.Assets.Count = 0 Then Throw New ArgumentException(T("没有可下载的更新包"))
        Dim Asset = Info.Assets(0)
        Dim Folder = PathTemp & "Update\" & Info.VersionCode & "\"
        Dim FileName = Path.GetFileName(Asset.Name)
        If String.IsNullOrWhiteSpace(FileName) Then FileName = "FoxesLauncher.exe"
        Dim Target = Folder & FileName
        DirectoryUtils.Create(Folder)
        If FileUtils.Exists(Target) Then FileUtils.Delete(Target)

        NetDownloadByLoader(Asset.Urls, Target, LoaderToSyncProgress,
            New FileChecker With {.ActualSize = If(Asset.Size > 0, Asset.Size, -1), .Hash = Asset.Sha256})

        Dim SignError As String = Nothing
        If Not ModRemoteCrypto.VerifyAuthenticode(Target, SignError) Then
            FileUtils.Delete(Target)
            Throw New Exception(T("更新包代码签名校验失败：{0}", SignError))
        End If

        '保存哈希旁车，供独立更新器进程在替换前再次校验。
        Try
            FileUtils.Write(Target & ".sha256", Asset.Sha256.Trim().ToLowerInvariant())
        Catch ex As Exception
            Logger.Warn(ex, "写入更新包哈希失败")
        End Try

        PendingUpdateInfo = Info
        PendingUpdatePath = Target
        IsUpdateWaitingRestart = True
        Return Target
    End Function

    ''' <summary>
    ''' 退出时启动更新器：等待旧进程退出，备份并替换文件，然后重启。
    ''' </summary>
    Friend Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        Try
            If String.IsNullOrWhiteSpace(PendingUpdatePath) OrElse Not FileUtils.Exists(PendingUpdatePath) Then
                IsUpdateWaitingRestart = False
                If TriggerRestartAndByEnd Then Hint(T("没有待安装的更新包。"), HintType.Red)
                Return
            End If
            If Not ModRemoteCrypto.VerifyAuthenticode(PendingUpdatePath) Then
                IsUpdateWaitingRestart = False
                Throw New Exception(T("待安装的更新包代码签名无效"))
            End If

            Dim Args = $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PendingUpdatePath}"" 1"
            Dim StartInfo As New ProcessStartInfo(PathExe, Args) With {.WorkingDirectory = Path.GetDirectoryName(PathExe)}
            Try
                '若安装目录不可写，则请求 UAC 提权运行同一个更新器。
                CheckPermissionWithException(Path.GetDirectoryName(PathExe) & "\")
            Catch
                StartInfo.UseShellExecute = True
                StartInfo.Verb = "runas"
            End Try
            StartProcess(StartInfo)
            IsUpdateWaitingRestart = False
            Logger.Info($"已启动更新器：{PendingUpdatePath}")
            If TriggerRestartAndByEnd Then FormMain.EndProgramForce(ProcessReturnValues.Success)
        Catch ex As Exception
            Logger.Error(ex, "启动更新器失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 由 --update 命令行参数调用。运行在独立的更新器进程中。
    ''' </summary>
    Friend Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            '等待旧进程退出
            Try
                Dim OldProcess = Process.GetProcessById(ProcessId)
                If Not OldProcess.WaitForExit(60000) Then
                    Throw New TimeoutException(T("等待旧版本退出超时"))
                End If
            Catch ex As ArgumentException
                '进程已经退出
            End Try

            If Not FileUtils.Exists(NewFileName) Then Throw New FileNotFoundException(T("找不到新的更新包"), NewFileName)
            Dim HashFile = NewFileName & ".sha256"
            If FileUtils.Exists(HashFile) Then
                Dim Expected = FileUtils.ReadAsString(HashFile).Trim().ToLowerInvariant()
                Dim Actual = CryptographyUtils.ComputeFileHash(NewFileName, CryptographyUtils.HashMethod.Sha256)
                If Not Actual.Equals(Expected, StringComparison.OrdinalIgnoreCase) Then
                    Throw New Exception(T("更新包 SHA-256 校验失败（期望 {0}，实际 {1}）", Expected, Actual))
                End If
            End If
            Dim SignError As String = Nothing
            If Not ModRemoteCrypto.VerifyAuthenticode(NewFileName, SignError) Then
                Throw New Exception(T("更新包代码签名校验失败：{0}", SignError))
            End If

            Dim Backup = OldFileName & ".bak"
            If FileUtils.Exists(Backup) Then FileUtils.Delete(Backup)
            If FileUtils.Exists(OldFileName) Then FileUtils.Move(OldFileName, Backup)

            Dim Replaced As Boolean = False
            Dim LastError As Exception = Nothing
            For i = 1 To 60
                Try
                    File.Copy(NewFileName, OldFileName, True)
                    Replaced = True
                    Exit For
                Catch ex As Exception
                    LastError = ex
                    Thread.Sleep(500)
                End Try
            Next

            If Not Replaced Then
                '回滚
                Try
                    If FileUtils.Exists(OldFileName) Then FileUtils.Delete(OldFileName)
                    If FileUtils.Exists(Backup) Then FileUtils.Move(Backup, OldFileName)
                Catch ex As Exception
                    Logger.Error(ex, "回滚更新失败")
                End Try
                Throw New Exception(T("替换启动器文件失败：{0}", If(LastError?.Message, T("未知错误"))))
            End If

            Try
                FileUtils.Delete(Backup)
                If Not PathUtils.IsSame(NewFileName, OldFileName) Then FileUtils.Delete(NewFileName)
                If FileUtils.Exists(HashFile) Then FileUtils.Delete(HashFile)
            Catch ex As Exception
                Logger.Warn(ex, "清理更新临时文件失败")
            End Try

            Logger.Info($"更新完成：{NewFileName} → {OldFileName}")
            Dim NewVersionCode = Path.GetFileName(Path.GetDirectoryName(NewFileName))
            Telemetry("UpdateSucceeded", "FromVersionCode", VersionCode.ToString(), "ToVersionCode", NewVersionCode)
            If TriggerRestart Then
                Try
                    StartProcess(New ProcessStartInfo(OldFileName, "--wait") With {.WorkingDirectory = Path.GetDirectoryName(OldFileName)})
                Catch ex As Exception
                    Logger.Error(ex, "更新完成后重启启动器失败")
                End Try
            End If
        Catch ex As Exception
            Logger.Error(ex, "执行更新替换失败")
            MsgBox(T("更新失败：{0}", ex.GetDisplay(True)), MsgBoxStyle.Critical, T("更新失败"))
        End Try
    End Sub

    ''' <summary>
    ''' 确保 PathTemp\Latest.exe 是最新正式版 FXL，供整合包打包使用。
    ''' </summary>
    Friend Sub DownloadLatestFXL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Dim Target = PathTemp & "Latest.exe"
        Try
            Dim OldChannel = Settings.Get(Of String)("UpdateChannel")
            Settings.Set("UpdateChannel", "official")
            Dim Result As UpdateCheckResult
            Try
                Result = CheckForUpdate(True, True)
            Finally
                Settings.Set("UpdateChannel", OldChannel)
            End Try

            If Result.Success AndAlso Result.Info IsNot Nothing AndAlso Result.Info.Assets.Count > 0 Then
                Dim Asset = Result.Info.Assets(0)
                If FileUtils.Exists(Target) Then FileUtils.Delete(Target)
                NetDownloadByLoader(Asset.Urls, Target, LoaderToSyncProgress,
                    New FileChecker With {.ActualSize = If(Asset.Size > 0, Asset.Size, -1), .Hash = Asset.Sha256})
                Logger.Info($"已下载最新正式版 FXL：{Result.Info.VersionName} ({Result.Info.VersionCode})")
                Return
            End If
        Catch ex As Exception
            Logger.Warn(ex, "下载最新正式版 FXL 失败，将回退到当前版本")
        End Try

        Try
            If Not PathUtils.IsSame(PathExe, Target) Then
                DirectoryUtils.Create(Path.GetDirectoryName(Target) & "\")
                File.Copy(PathExe, Target, True)
                Logger.Info("已使用当前版本作为 Latest.exe")
            End If
        Catch ex As Exception
            Logger.Error(ex, "复制当前版本为 Latest.exe 失败")
        End Try
    End Sub

#End Region

#Region "TLS 软钉扎"

    Private TlsSoftPinChecked As Boolean

    ''' <summary>
    ''' 仅做辅助告警：证书链 CA 不在允许列表时上报，但不阻止更新。
    ''' </summary>
    Private Sub CheckTlsSoftPinIfNeeded()
        If TlsSoftPinChecked Then Return
        TlsSoftPinChecked = True
        If String.IsNullOrWhiteSpace(TlsPinnedCaThumbprints) Then Return
        RunInNewThread(
        Sub()
            Try
                Dim Hosts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each Source In BuildUpdateSources()
                    Try
                        Dim Uri = New Uri(Source.Url)
                        If Uri.Scheme = "https" Then Hosts.Add(Uri.Host)
                    Catch
                    End Try
                Next
                For Each Host In Hosts
                    Try
                        Dim Request = DirectCast(WebRequest.Create($"https://{Host}/"), HttpWebRequest)
                        Request.Method = "HEAD"
                        Request.Timeout = 5000
                        Request.AllowAutoRedirect = False
                        Using Response = Request.GetResponse()
                        End Using
                        Dim Certificate As X509Certificate2 = Nothing
                        If Request.ServicePoint.Certificate IsNot Nothing Then
                            Certificate = New X509Certificate2(Request.ServicePoint.Certificate)
                        End If
                        If Certificate Is Nothing Then Continue For
                        Dim Chain As New X509Chain()
                        Chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck
                        Chain.Build(Certificate)
                        Dim Pins = TlsPinnedCaThumbprints.Split(";"c).
                            Select(Function(p) p.Replace(" ", "").Replace("-", "").Trim()).
                            Where(Function(p) p.Length > 0)
                        Dim Matched = Chain.ChainElements.Cast(Of X509ChainElement)().
                            Any(Function(e) Pins.Any(Function(p) e.Certificate.Thumbprint.Equals(p, StringComparison.OrdinalIgnoreCase)))
                        If Not Matched Then
                            Telemetry("TlsPinMismatch", "Host", Host, "ExceptionCategory", "tls-pin")
                            Logger.Warn($"TLS 软钉扎不匹配：{Host}")
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, $"TLS 软钉扎检查失败：{Host}")
                    End Try
                Next
            Catch ex As Exception
                Logger.Warn(ex, "TLS 软钉扎检查异常")
            End Try
        End Sub, "TLS Soft Pin", ThreadPriority.Lowest)
    End Sub

#End Region

End Module
