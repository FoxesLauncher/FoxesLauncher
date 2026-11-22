Imports System.Globalization

''' <summary>
''' 签名公告与动态配置。公告只读展示，不包含任何远程执行动作。
''' </summary>
Friend Module ModRemoteContent

    Private NoticesLoaded As Boolean
    Private Const NoticeCacheMinutes As Integer = 10

    ''' <summary>
    ''' 拉取并验签动态配置（联机等设置）。
    ''' </summary>
    Friend Sub LoadServerConfig()
        Try
            If String.IsNullOrWhiteSpace(UpdateConfigUrl) Then Return
            Dim Raw = NetRequestByClient(UpdateConfigUrl, Timeout:=10000, MakeLog:=False)
            Dim Document As ModRemoteCrypto.RemoteSignedDocument = Nothing
            Dim ErrorMessage As String = Nothing
            If Not ModRemoteCrypto.VerifySignedDocument(Raw, Document, ErrorMessage) Then
                Throw New Exception(T("配置验签失败：{0}", ErrorMessage))
            End If
            ServerConfig = Document.Payload
            Logger.Info($"已加载远程配置（keyId={Document.KeyId}）")
        Catch ex As Exception
            Logger.Warn(ex, "获取远程配置失败，将使用上次有效配置")
        End Try
    End Sub

    ''' <summary>
    ''' 启动后后台拉取公告。失败不影响启动。
    ''' </summary>
    Friend Sub StartNotices()
        If NoticesLoaded Then Return
        NoticesLoaded = True
        RunInNewThread(
        Sub()
            Try
                Thread.Sleep(3000)
                Dim Raw = GetNoticesRaw()
                If String.IsNullOrWhiteSpace(Raw) Then Return
                Dim Document As ModRemoteCrypto.RemoteSignedDocument = Nothing
                Dim ErrorMessage As String = Nothing
                If Not ModRemoteCrypto.VerifySignedDocument(Raw, Document, ErrorMessage) Then
                    Logger.Warn($"公告验签失败：{ErrorMessage}")
                    Return
                End If
                Dim Notices = ParseNotices(Document.Payload)
                If Notices.Count > 0 Then RunInUi(Sub() ShowNotices(Notices))
            Catch ex As Exception
                Logger.Warn(ex, "获取公告失败")
            End Try
        End Sub, "Notices", ThreadPriority.Lowest)
    End Sub

    Private Function GetNoticesRaw() As String
        Try
            Dim CacheFile = PathTemp & "Cache\Notices\official.json"
            Dim TimeFile = PathTemp & "Cache\Notices\official.time"
            If FileUtils.Exists(CacheFile) AndAlso FileUtils.Exists(TimeFile) Then
                Dim CachedAt As Long
                If Long.TryParse(FileUtils.ReadAsString(TimeFile).Trim(), CachedAt) AndAlso
                   DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - CachedAt <= NoticeCacheMinutes * 60 * 1000 Then
                    Return FileUtils.ReadAsString(CacheFile)
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取公告缓存失败")
        End Try

        Dim Urls As New List(Of String) From {UpdateNoticesUrl}
        If Not String.IsNullOrWhiteSpace(UpdateNoticesFallbacks) Then
            Urls.AddRange(UpdateNoticesFallbacks.Split(","c).
                Select(Function(u) u.Trim()).
                Where(Function(u) u.Length > 0))
        End If

        For Each Url In Urls
            If String.IsNullOrWhiteSpace(Url) Then Continue For
            Try
                Dim Raw = NetRequestByClient(Url, Timeout:=10000, MakeLog:=False)
                If Not String.IsNullOrWhiteSpace(Raw) Then
                    Try
                        DirectoryUtils.Create(PathTemp & "Cache\Notices\")
                        FileUtils.Write(PathTemp & "Cache\Notices\official.json", Raw)
                        FileUtils.Write(PathTemp & "Cache\Notices\official.time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
                    Catch ex As Exception
                        Logger.Warn(ex, "写入公告缓存失败")
                    End Try
                    Return Raw
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"从 {Url} 获取公告失败")
            End Try
        Next
        Return Nothing
    End Function

    Private Class NoticeItem
        Public Id As Integer
        Public Level As String
        Public Title As String
        Public Body As String
        Public Dismissible As Boolean
        Public Link As String
    End Class

    Private Function ParseNotices(Payload As JObject) As List(Of NoticeItem)
        Dim Result As New List(Of NoticeItem)
        If Payload Is Nothing Then Return Result
        Dim Revision = ToInt(Payload("revision"))
        If Settings.Get(Of Integer)("NoticeRevision") <> Revision Then
            Settings.Set("NoticeRevision", Revision)
            Settings.Set("NoticeDismissed", "")
        End If

        Dim Dismissed = GetDismissedIds()
        Dim Items = TryCast(Payload("notices"), JArray)
        If Items Is Nothing Then Return Result

        For Each Token In Items
            Dim Node = TryCast(Token, JObject)
            If Node Is Nothing Then Continue For
            Dim Id = ToInt(Node("id"))
            If Id <= 0 OrElse Dismissed.Contains(Id) Then Continue For

            Dim MinVersion = ToInt(Node("minVersionCode"))
            If MinVersion > 0 AndAlso VersionCode < MinVersion Then Continue For
            Dim MaxToken = Node("maxVersionCode")
            If MaxToken IsNot Nothing AndAlso MaxToken.Type <> JTokenType.Null Then
                Dim MaxVersion = ToInt(MaxToken)
                If MaxVersion > 0 AndAlso VersionCode > MaxVersion Then Continue For
            End If

            If Not IsNoticeInTimeWindow(Node, "startAt", "endAt") Then Continue For
            If Not IsNoticeForLocale(Node("locales")) Then Continue For

            Dim Level = If(Node("level")?.ToString, "info").ToLowerInvariant()
            Dim Link = If(Node("link")?.ToString, "")
            If Link.Length > 0 AndAlso Not Link.StartsWithF("https://", True) Then Link = ""

            Result.Add(New NoticeItem With {
                .Id = Id,
                .Level = Level,
                .Title = If(Node("title")?.ToString, T("公告")),
                .Body = If(Node("bodyMarkdown")?.ToString, ""),
                .Dismissible = ToBool(Node("dismissible"), True),
                .Link = Link
            })
        Next
        Return Result
    End Function

    Private Function IsNoticeInTimeWindow(Node As JObject, StartKey As String, EndKey As String) As Boolean
        Dim NowUtc = DateTime.UtcNow
        Dim StartText = Node(StartKey)?.ToString
        Dim EndText = Node(EndKey)?.ToString
        Dim StartTime As Date = Date.MinValue
        Dim EndTime As Date = Date.MinValue
        If Not String.IsNullOrWhiteSpace(StartText) AndAlso
           Date.TryParse(StartText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, StartTime) AndAlso
           NowUtc < StartTime Then Return False
        If Not String.IsNullOrWhiteSpace(EndText) AndAlso
           Date.TryParse(EndText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, EndTime) AndAlso
           NowUtc > EndTime Then Return False
        Return True
    End Function

    Private Function IsNoticeForLocale(Locales As JToken) As Boolean
        If Locales Is Nothing OrElse Locales.Type <> JTokenType.Array OrElse Locales.Count = 0 Then Return True
        Dim Current = ModRegion.CurrentLanguage
        For Each Token In DirectCast(Locales, JArray)
            Dim Code = Token?.ToString
            If String.IsNullOrWhiteSpace(Code) OrElse Code = "all" Then Return True
            If Code.Equals(Current, StringComparison.OrdinalIgnoreCase) Then Return True
            'zh_cn 与 zh-CN、zh 等写法兼容
            If Code.Replace("-", "_").Equals(Current, StringComparison.OrdinalIgnoreCase) Then Return True
            If Current.StartsWithF(Code.Replace("-", "_") & "_", True) Then Return True
        Next
        Return False
    End Function

    Private Function GetDismissedIds() As HashSet(Of Integer)
        Dim Result As New HashSet(Of Integer)
        Try
            For Each Part In Settings.Get(Of String)("NoticeDismissed").Split(","c)
                Dim Id As Integer
                If Integer.TryParse(Part.Trim(), Id) Then Result.Add(Id)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取已关闭公告失败")
        End Try
        Return Result
    End Function

    Private Sub DismissNotice(Id As Integer)
        Try
            Dim Ids = GetDismissedIds()
            Ids.Add(Id)
            Settings.Set("NoticeDismissed", String.Join(",", Ids.Select(Function(i) i.ToString())))
        Catch ex As Exception
            Logger.Warn(ex, "记录已关闭公告失败")
        End Try
    End Sub

    Private Sub ShowNotices(Notices As List(Of NoticeItem))
        Dim Shown As Integer = 0
        For Each Notice In Notices
            If Shown >= 3 Then Exit For
            Shown += 1
            Telemetry("NoticeShown", "NoticeId", Notice.Id.ToString(), "Level", Notice.Level)

            Dim Body = Notice.Body
            If Body.Length > 1500 Then Body = Body.Substring(0, 1500) & "..."
            Dim Message = Body
            If Notice.Link.Length > 0 Then Message &= vbCrLf & vbCrLf & T("相关链接：{0}", Notice.Link)
            Dim IsWarn = Notice.Level = "warn" OrElse Notice.Level = "critical"

            Dim Choice As Integer
            If Notice.Link.Length > 0 Then
                Dim Link = Notice.Link
                Choice = MyMsgBox(Message, Notice.Title,
                    If(Notice.Dismissible, T("我知道了"), T("关闭")),
                    If(Notice.Dismissible, T("稍后提醒"), ""),
                    T("打开链接"),
                    IsWarn:=IsWarn,
                    ForceWait:=Not Notice.Dismissible,
                    Button3Action:=Sub()
                                        Telemetry("NoticeClicked", "NoticeId", Notice.Id.ToString(), "Kind", "link")
                                        OpenWebsite(Link)
                                    End Sub)
            Else
                Choice = MyMsgBox(Message, Notice.Title,
                    If(Notice.Dismissible, T("我知道了"), T("关闭")),
                    If(Notice.Dismissible, T("稍后提醒"), ""),
                    IsWarn:=IsWarn,
                    ForceWait:=Not Notice.Dismissible)
            End If

            If Notice.Dismissible AndAlso (Choice = 1 OrElse Choice = 3) Then DismissNotice(Notice.Id)
        Next
    End Sub

    Private Function ToInt(Token As JToken, Optional DefaultValue As Integer = 0) As Integer
        If Token Is Nothing Then Return DefaultValue
        Try
            Return Token.ToObject(Of Integer)()
        Catch
            Return DefaultValue
        End Try
    End Function

    Private Function ToBool(Token As JToken, Optional DefaultValue As Boolean = False) As Boolean
        If Token Is Nothing Then Return DefaultValue
        Try
            Return Token.ToObject(Of Boolean)()
        Catch
            Return DefaultValue
        End Try
    End Function

End Module
