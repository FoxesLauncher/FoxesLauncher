''' <summary>
''' 资源包字体安全检查，用于防御 Minecraft2Shell 一类通过 TrueType hinting 触发的漏洞。
''' </summary>
Public Module ModFontGuard

    ''' <summary>
    ''' 单个字体文件的检查结果。
    ''' </summary>
    Public Class FontGuardFont
        Public EntryName As String = ""
        ''' <summary>
        ''' 是否包含 hinting 字节码（fpgm、prep 或字形指令）。
        ''' </summary>
        Public HasHinting As Boolean = False
        Public Detail As String = ""
    End Class

    ''' <summary>
    ''' 单个资源包的检查结果。
    ''' </summary>
    Public Class FontGuardPack
        Public PackPath As String = ""
        Public PackName As String = ""
        Public Fonts As New List(Of FontGuardFont)
        Public ScanError As String = ""
        Public ReadOnly Property HasHinting As Boolean
            Get
                Return Fonts.Any(Function(f) f.HasHinting)
            End Get
        End Property
    End Class

    '单个字体文件最多读取 32 MB，避免异常文件占用过多内存
    Private Const MaxFontBytes As Long = 32L * 1024 * 1024
    '每个字体最多检查 4000 个字形的指令，避免超大字体拖慢启动
    Private Const MaxGlyphChecks As Integer = 4000
    Private Const QuarantineFolder As String = "Quarantine\ResourcePacks\"

#Region "资源包扫描"

    Private ReadOnly PackCache As New ConcurrentDictionary(Of String, FontGuardPack)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 检查资源包中是否包含带 hinting 指令的 TrueType 字体。
    ''' </summary>
    Public Function FontGuardScanPack(PackPath As String) As FontGuardPack
        Dim Result As New FontGuardPack With {
            .PackPath = PackPath,
            .PackName = PathUtils.GetLastPart(PackPath)
        }
        Try
            Using Zip = FileUtils.OpenZip(PackPath)
                For Each Entry In Zip.Entries
                    If Entry.Length <= 0 Then Continue For
                    Dim Extension As String = PathUtils.GetExtension(Entry.FullName).ToLowerInvariant()
                    If Extension <> "ttf" AndAlso Extension <> "otf" AndAlso Extension <> "ttc" Then Continue For
                    Dim Font As New FontGuardFont With {.EntryName = Entry.FullName}
                    If Entry.Length > MaxFontBytes Then
                        Font.Detail = "字体文件过大，已跳过检查"
                        Result.Fonts.Add(Font)
                        Continue For
                    End If
                    Try
                        Using Stream = Entry.Open()
                            Dim Data = FontGuardReadStream(Stream, MaxFontBytes)
                            Dim Detail As String = ""
                            Font.HasHinting = FontGuardInspectFont(Data, Detail)
                            Font.Detail = Detail
                        End Using
                    Catch ex As Exception
                        Font.Detail = "读取失败"
                        Logger.Warn(ex, $"检查资源包字体失败：{PackPath} → {Entry.FullName}")
                    End Try
                    Result.Fonts.Add(Font)
                Next
            End Using
        Catch ex As Exception
            Result.ScanError = ex.Message
            Logger.Warn(ex, $"检查资源包失败：{PackPath}")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 带缓存的资源包扫描，缓存依据路径、大小与修改时间。
    ''' </summary>
    Public Function FontGuardScanPackCached(PackPath As String) As FontGuardPack
        Dim Key As String = FontGuardPackKey(PackPath)
        Dim Cached As FontGuardPack = Nothing
        If PackCache.TryGetValue(Key, Cached) Then Return Cached
        Cached = FontGuardScanPack(PackPath)
        '缓存条目过多时清空，避免长期运行积累
        If PackCache.Count > 256 Then PackCache.Clear()
        PackCache(Key) = Cached
        Return Cached
    End Function

    Private Function FontGuardPackKey(PackPath As String) As String
        Try
            Dim Info = FileUtils.GetInfo(PackPath)
            Return PathUtils.ForCompare(PackPath) & "|" & Info.Length.ToString() & "|" & Info.LastWriteTimeUtc.Ticks.ToString()
        Catch
            Return PathUtils.ForCompare(PackPath)
        End Try
    End Function

    Private Function FontGuardReadStream(Stream As IO.Stream, Limit As Long) As Byte()
        Using Memory As New MemoryStream()
            Dim Buffer(65535) As Byte
            Dim Total As Long = 0
            Do
                Dim ReadCount As Integer = Stream.Read(Buffer, 0, Buffer.Length)
                If ReadCount <= 0 Then Exit Do
                Total += ReadCount
                If Total > Limit Then Exit Do
                Memory.Write(Buffer, 0, ReadCount)
            Loop
            Return Memory.ToArray()
        End Using
    End Function

    ''' <summary>
    ''' 读取资源包前 4 个字节，判断是否为 ZIP。服务器的资源包缓存没有扩展名，不能用后缀判断。
    ''' </summary>
    Public Function FontGuardLooksLikeZip(PackPath As String) As Boolean
        Try
            Using Stream As New FileStream(PackPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                If Stream.Length < 4 Then Return False
                Dim Buffer(3) As Byte
                If Stream.Read(Buffer, 0, 4) <> 4 Then Return False
                Return Buffer(0) = &H50 AndAlso Buffer(1) = &H4B AndAlso
                       ((Buffer(2) = 3 AndAlso Buffer(3) = 4) OrElse (Buffer(2) = 5 AndAlso Buffer(3) = 6))
            End Using
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "sfnt 解析"

    ''' <summary>
    ''' 判断字体是否包含 TrueType hinting 字节码。OTTO（CFF）字体没有这类指令，会直接跳过。
    ''' </summary>
    Private Function FontGuardInspectFont(Data As Byte(), ByRef Detail As String) As Boolean
        If Data Is Nothing OrElse Data.Length < 12 Then Return False

        'Sfnt 头：0x00010000、true 为 TrueType，OTTO 为 CFF，ttcf 为字体集合
        Dim Faces As New List(Of Integer)
        Dim Tag As String = FontGuardReadTag(Data, 0)
        If Tag = "ttcf" Then
            Dim FaceCount As Long = FontGuardReadU32(Data, 8)
            If FaceCount < 0 Then Return False
            FaceCount = Math.Min(FaceCount, 16)
            For i = 0 To CInt(FaceCount) - 1
                Dim Offset As Long = FontGuardReadU32(Data, 12 + i * 4)
                If Offset >= 0 AndAlso Offset + 12 <= Data.Length Then Faces.Add(CInt(Offset))
            Next
        ElseIf Tag = "OTTO" OrElse Tag = "true" OrElse Tag = "typ1" OrElse FontGuardReadU16(Data, 0) = &H1 Then
            Faces.Add(0)
        Else
            Return False
        End If

        For Each Face In Faces
            Dim Fpgm As Boolean = False, Prep As Boolean = False
            Dim GlyfOffset As Long = -1, GlyfLength As Long = -1
            Dim LocaOffset As Long = -1, HeadOffset As Long = -1, MaxpOffset As Long = -1

            Dim TableCount As Integer = FontGuardReadU16(Data, Face + 4)
            If TableCount < 0 Then Continue For
            For i = 0 To Math.Min(TableCount, 512) - 1
                Dim Record As Integer = Face + 12 + i * 16
                If Record + 16 > Data.Length Then Exit For
                Dim TableTag As String = FontGuardReadTag(Data, Record)
                Dim TableOffset As Long = FontGuardReadU32(Data, Record + 8)
                Dim TableLength As Long = FontGuardReadU32(Data, Record + 12)
                If TableOffset < 0 OrElse TableLength < 0 OrElse TableOffset + TableLength > Data.Length Then Continue For
                Select Case TableTag
                    Case "fpgm"
                        If TableLength > 0 Then Fpgm = True
                    Case "prep"
                        If TableLength > 0 Then Prep = True
                    Case "glyf"
                        GlyfOffset = TableOffset : GlyfLength = TableLength
                    Case "loca"
                        LocaOffset = TableOffset
                    Case "head"
                        HeadOffset = TableOffset
                    Case "maxp"
                        MaxpOffset = TableOffset
                End Select
            Next

            Dim Reasons As New List(Of String)
            If Fpgm Then Reasons.Add("fpgm")
            If Prep Then Reasons.Add("prep")
            Dim GlyphInstructions As Boolean = FontGuardHasGlyphInstructions(Data, GlyfOffset, GlyfLength, LocaOffset, HeadOffset, MaxpOffset)
            If GlyphInstructions Then Reasons.Add("glyf")
            If Reasons.Any() Then
                Detail = "包含 hinting 指令：" & Reasons.Join("、")
                Return True
            End If
        Next

        Detail = "未发现 hinting 指令"
        Return False
    End Function

    ''' <summary>
    ''' 检查字形本身是否携带指令。某些字体没有 fpgm 或 prep，但仍可在字形中执行字节码。
    ''' </summary>
    Private Function FontGuardHasGlyphInstructions(Data As Byte(), GlyfOffset As Long, GlyfLength As Long,
                                                  LocaOffset As Long, HeadOffset As Long, MaxpOffset As Long) As Boolean
        If GlyfOffset < 0 OrElse GlyfLength <= 0 OrElse LocaOffset < 0 OrElse HeadOffset < 0 OrElse MaxpOffset < 0 Then Return False
        If HeadOffset + 52 > Data.Length OrElse MaxpOffset + 6 > Data.Length Then Return False

        Dim IndexToLocFormat As Integer = FontGuardReadU16(Data, CInt(HeadOffset + 50))
        Dim GlyphCount As Integer = FontGuardReadU16(Data, CInt(MaxpOffset + 4))
        If GlyphCount <= 0 Then Return False
        Dim Limit As Integer = Math.Min(GlyphCount, MaxGlyphChecks)

        For i = 0 To Limit - 1
            Dim Start As Long = FontGuardReadLoca(Data, LocaOffset, i, IndexToLocFormat)
            Dim [End] As Long = FontGuardReadLoca(Data, LocaOffset, i + 1, IndexToLocFormat)
            If Start < 0 OrElse [End] < 0 OrElse [End] <= Start Then Continue For
            If Start + 12 > GlyfLength Then Continue For

            Dim Glyph As Long = GlyfOffset + Start
            If Glyph + 10 > Data.Length Then Continue For
            Dim Contours As Integer = FontGuardReadI16(Data, CInt(Glyph))
            If Contours < 0 Then Continue For '复合字形，没有指令
            Dim InstructionPos As Long = Glyph + 10 + Contours * 2
            If InstructionPos + 2 > Data.Length Then Continue For
            If FontGuardReadU16(Data, CInt(InstructionPos)) > 0 Then Return True
        Next
        Return False
    End Function

    Private Function FontGuardReadLoca(Data As Byte(), LocaOffset As Long, Index As Integer, IndexToLocFormat As Integer) As Long
        If IndexToLocFormat = 0 Then
            Dim Offset As Long = LocaOffset + Index * 2
            If Offset + 2 > Data.Length Then Return -1
            Return FontGuardReadU16(Data, CInt(Offset)) * 2
        Else
            Dim Offset As Long = LocaOffset + Index * 4
            Return FontGuardReadU32(Data, CInt(Offset))
        End If
    End Function

    Private Function FontGuardReadTag(Data As Byte(), Offset As Integer) As String
        If Offset < 0 OrElse Offset + 4 > Data.Length Then Return ""
        Return New String({ChrW(Data(Offset)), ChrW(Data(Offset + 1)), ChrW(Data(Offset + 2)), ChrW(Data(Offset + 3))})
    End Function

    Private Function FontGuardReadU16(Data As Byte(), Offset As Integer) As Integer
        If Offset < 0 OrElse Offset + 2 > Data.Length Then Return -1
        Return (CInt(Data(Offset)) << 8) Or CInt(Data(Offset + 1))
    End Function

    Private Function FontGuardReadI16(Data As Byte(), Offset As Integer) As Integer
        Dim Value As Integer = FontGuardReadU16(Data, Offset)
        If Value < 0 Then Return -1
        If Value >= &H8000 Then Return Value - &H10000
        Return Value
    End Function

    Private Function FontGuardReadU32(Data As Byte(), Offset As Integer) As Long
        If Offset < 0 OrElse Offset + 4 > Data.Length Then Return -1
        Return (CLng(Data(Offset)) << 24) Or (CLng(Data(Offset + 1)) << 16) Or
               (CLng(Data(Offset + 2)) << 8) Or CLng(Data(Offset + 3))
    End Function

#End Region

#Region "隔离与已提示记录"

    ''' <summary>
    ''' 将资源包移入隔离区。返回移动后的路径，失败时返回空字符串。
    ''' </summary>
    Public Function FontGuardQuarantine(PackPath As String) As String
        Try
            Dim Folder As String = PathTemp & QuarantineFolder
            DirectoryUtils.Create(Folder)
            Dim BaseName As String = Date.Now.ToString("yyyyMMdd-HHmmss") & "_" & PathUtils.GetLastPart(PackPath)
            Dim Target As String = Folder & BaseName
            Dim Index As Integer = 1
            Do While FileUtils.Exists(Target)
                Target = Folder & BaseName & " (" & Index.ToString() & ")"
                Index += 1
            Loop
            FileUtils.Move(PackPath, Target)
            Logger.Warn($"已将疑似携带字体漏洞的资源包移入隔离区：{PackPath} → {Target}")
            Return Target
        Catch ex As Exception
            Logger.Warn(ex, $"隔离资源包失败：{PackPath}")
            Return ""
        End Try
    End Function

    Private ReadOnly KnownLock As New Object
    Private KnownList As List(Of String) = Nothing
    Private KnownDirty As Boolean = False

    Private ReadOnly Property KnownFile As String
        Get
            Return PathTemp & "Cache\FontGuard\known.txt"
        End Get
    End Property

    Private Function FontGuardKnownKey(PackPath As String) As String
        Try
            Dim Info = FileUtils.GetInfo(PackPath)
            Return PathUtils.ForCompare(PackPath) & "|" & Info.Length.ToString()
        Catch
            Return PathUtils.ForCompare(PackPath)
        End Try
    End Function

    Private Function FontGuardGetKnown() As List(Of String)
        If KnownList IsNot Nothing Then Return KnownList
        KnownList = New List(Of String)
        Try
            If FileUtils.Exists(KnownFile) Then
                KnownList.AddRange(FileUtils.ReadAsString(KnownFile).Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries))
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取资源包字体检查记录失败")
        End Try
        Return KnownList
    End Function

    Private Function FontGuardIsKnown(PackPath As String) As Boolean
        SyncLock KnownLock
            Return FontGuardGetKnown().Contains(FontGuardKnownKey(PackPath), StringComparer.OrdinalIgnoreCase)
        End SyncLock
    End Function

    Private Sub FontGuardMarkKnown(Packs As IEnumerable(Of FontGuardPack))
        SyncLock KnownLock
            Dim List = FontGuardGetKnown()
            For Each Pack In Packs
                Dim Key = FontGuardKnownKey(Pack.PackPath)
                If Not List.Contains(Key, StringComparer.OrdinalIgnoreCase) Then
                    List.Add(Key)
                    KnownDirty = True
                End If
            Next
            If List.Count > 300 Then
                List.RemoveRange(0, List.Count - 300)
                KnownDirty = True
            End If
            If Not KnownDirty Then Return
            Try
                DirectoryUtils.Create(PathUtils.RemoveLastPart(KnownFile))
                FileUtils.Write(KnownFile, String.Join(vbLf, List))
                KnownDirty = False
            Catch ex As Exception
                Logger.Warn(ex, "写入资源包字体检查记录失败")
            End Try
        End SyncLock
    End Sub

#End Region

#Region "启动前与下载后检查"

    Private Function FontGuardEnabled() As Boolean
        Try
            Return Settings.Get(Of Boolean)("LaunchAdvanceFontGuard")
        Catch
            Return True
        End Try
    End Function

    ''' <summary>
    ''' 收集实例内可能存在风险的资源包。
    ''' </summary>
    Private Function FontGuardCollectRisks(Instance As McInstance) As List(Of FontGuardPack)
        Dim Result As New List(Of FontGuardPack)
        Dim Folders As String() = {
            Instance.PathIndie & "resourcepacks\",
            Instance.PathIndie & "server-resource-packs\"
        }
        For Each Folder In Folders
            Try
                If Not DirectoryUtils.Exists(Folder) Then Continue For
                For Each File In DirectoryUtils.EnumerateFiles(Folder)
                    If PathUtils.GetLastPart(File).Equals("FXL Skin.zip", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not FontGuardLooksLikeZip(File) Then Continue For
                    Dim Risk = FontGuardScanPackCached(File)
                    If Risk.HasHinting Then Result.Add(Risk)
                Next
            Catch ex As Exception
                Logger.Warn(ex, $"枚举资源包文件夹失败：{Folder}")
            End Try
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 启动前扫描实例的资源包，对本地已有的可疑资源包只做提示，不自动移动。
    ''' </summary>
    Public Sub FontGuardCheckInstance(Instance As McInstance)
        If Instance Is Nothing OrElse Not FontGuardEnabled() Then Return
        If CurrentLaunchOptions?.SaveBatch IsNot Nothing Then Return '导出启动脚本时不弹窗
        Try
            Dim Risks = FontGuardCollectRisks(Instance).Where(Function(p) Not FontGuardIsKnown(p.PackPath)).ToList()
            If Not Risks.Any() Then Return

            Dim Names As String = vbCrLf & String.Join(vbCrLf, Risks.Take(8).Select(Function(p) "- " & p.PackName))
            If Risks.Count > 8 Then Names &= vbCrLf & "- ..."
            Dim Message As String = T("检测到 {0} 个资源包包含带 hinting 指令的字体：{1}{2}这类字体可能被用于 Minecraft2Shell 一类的漏洞攻击。要立即将它们移入隔离区吗？",
                                      Risks.Count, Names, vbCrLf & vbCrLf)
            Dim Choice As Integer = RunInUiWait(Function() MyMsgBox(Message, T("资源包字体风险"), T("移入隔离区"), T("暂不处理"), IsWarn:=True))
            If Choice = 1 Then
                For Each Risk In Risks
                    If FontGuardQuarantine(Risk.PackPath) = "" Then FontGuardMarkKnown({Risk})
                Next
            Else
                FontGuardMarkKnown(Risks)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "启动前检查资源包字体失败")
        End Try
    End Sub

    ''' <summary>
    ''' 启动器下载完成后检查资源包。命中时询问玩家是否隔离，适用于玩家主动下载的资源包。
    ''' </summary>
    Public Sub FontGuardAfterDownload(Files As IEnumerable(Of NetFile))
        If Not FontGuardEnabled() OrElse Files Is Nothing Then Return
        Try
            Dim Risks As New List(Of FontGuardPack)
            For Each File In Files
                If File Is Nothing OrElse File.State <> NetState.Finished OrElse String.IsNullOrWhiteSpace(File.LocalPath) Then Continue For
                If PathUtils.GetExtension(File.LocalPath).ToLowerInvariant() <> "zip" Then Continue For
                If Not File.LocalPath.Replace("/"c, "\"c).ContainsIgnoreCase("\resourcepacks\") Then Continue For
                If Not FileUtils.Exists(File.LocalPath) Then Continue For
                Dim Risk = FontGuardScanPackCached(File.LocalPath)
                If Risk.HasHinting Then Risks.Add(Risk)
            Next
            If Not Risks.Any() Then Return

            Dim Names As String = vbCrLf & String.Join(vbCrLf, Risks.Take(8).Select(Function(p) "- " & p.PackName))
            Dim Message As String = T("刚下载的资源包包含带 hinting 指令的字体：{0}{1}这类字体可能被用于 Minecraft2Shell 一类的漏洞攻击。要将其移入隔离区吗？",
                                      Names, vbCrLf & vbCrLf)
            Dim Choice As Integer = RunInUiWait(Function() MyMsgBox(Message, T("资源包字体风险"), T("移入隔离区"), T("保留"), IsWarn:=True))
            If Choice = 1 Then
                For Each Risk In Risks
                    If FontGuardQuarantine(Risk.PackPath) = "" Then FontGuardMarkKnown({Risk})
                Next
            Else
                FontGuardMarkKnown(Risks)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "检查下载的资源包字体失败")
        End Try
    End Sub

#End Region

#Region "运行期间监控"

    ''' <summary>
    ''' 游戏运行期间监控资源包文件夹。服务器推送的资源包会被自动隔离，本地资源包只提示一次。
    ''' </summary>
    Public Sub FontGuardStartWatcher(Watcher As Watcher)
        If Watcher Is Nothing OrElse Not FontGuardEnabled() Then Return
        RunInNewThread(Sub() FontGuardWatchLoop(Watcher), "FontGuard " & Watcher.Instance.Name, ThreadPriority.BelowNormal)
    End Sub

    Private Sub FontGuardWatchLoop(Watcher As Watcher)
        '路径 -> 上次看到的文件大小，-1 表示已检查完毕
        Dim Seen As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
        Dim Warned As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim Attempts As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Dim ServerFolder As String = Watcher.Instance.PathIndie & "server-resource-packs\"
        Dim LocalFolder As String = Watcher.Instance.PathIndie & "resourcepacks\"

        Do
            Try
                Dim State = Watcher.State
                If State = Watcher.MinecraftState.Ended OrElse State = Watcher.MinecraftState.Canceled Then Exit Do
                If Watcher.GameProcess Is Nothing OrElse Watcher.GameProcess.HasExited Then Exit Do

                FontGuardWatchFolder(ServerFolder, True, Seen, Warned, Attempts)
                FontGuardWatchFolder(LocalFolder, False, Seen, Warned, Attempts)
            Catch ex As Exception
                If TypeOf ex IsNot ThreadAbortException Then Logger.Warn(ex, "资源包字体监控出错")
            End Try
            Thread.Sleep(400)
        Loop
    End Sub

    Private Sub FontGuardWatchFolder(Folder As String, IsServerPack As Boolean,
                                     Seen As Dictionary(Of String, Long), Warned As HashSet(Of String),
                                     Attempts As Dictionary(Of String, Integer))
        If Not DirectoryUtils.Exists(Folder) Then Return
        For Each File In DirectoryUtils.EnumerateFiles(Folder)
            Try
                If PathUtils.GetLastPart(File).Equals("FXL Skin.zip", StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not IsServerPack AndAlso PathUtils.GetExtension(File).ToLowerInvariant() <> "zip" Then Continue For

                Dim Length As Long = FileUtils.GetInfo(File).Length
                If Length < 256 Then Continue For

                Dim Previous As Long
                If Not Seen.TryGetValue(File, Previous) Then
                    '新出现的文件，等待一次轮询确认写入完成
                    Seen(File) = Length
                    Continue For
                End If
                If Previous < 0 Then Continue For '已经检查过
                If Previous <> Length Then
                    Seen(File) = Length
                    Continue For
                End If

                If Not FontGuardLooksLikeZip(File) Then
                    Seen(File) = -1
                    Continue For
                End If
                Dim Risk = FontGuardScanPackCached(File)
                If Risk.ScanError <> "" Then
                    '文件可能仍在写入，最多重试 5 次
                    Dim Count As Integer = 0
                    Attempts.TryGetValue(File, Count)
                    Count += 1
                    Attempts(File) = Count
                    Seen(File) = If(Count >= 5, -1, Length)
                    Continue For
                End If
                Seen(File) = -1
                If Not Risk.HasHinting Then Continue For

                If IsServerPack Then
                    Dim Target = FontGuardQuarantine(File)
                    If Target <> "" Then
                        McLaunchLog($"已隔离服务器推送的高风险资源包：{Risk.PackName}", LogLevel.Warn)
                        Hint(T("已拦截服务器推送的高风险资源包：{0}", Risk.PackName), HintType.Red)
                    Else
                        McLaunchLog($"无法隔离服务器推送的高风险资源包：{Risk.PackPath}", LogLevel.Error)
                        Hint(T("检测到服务器推送的高风险资源包，但无法隔离，请立即退出游戏：{0}", Risk.PackName), HintType.Red)
                    End If
                ElseIf Warned.Add(File) Then
                    McLaunchLog($"检测到包含 hinting 字体的本地资源包：{Risk.PackPath}", LogLevel.Warn)
                    Hint(T("资源包 {0} 包含带 hinting 指令的字体，存在字体漏洞风险", Risk.PackName), HintType.Red)
                End If
            Catch ex As Exception
                '文件可能仍被游戏占用，下一轮再试
                Logger.Trace($"监控资源包失败：{File}，{ex.Message}")
            End Try
        Next
    End Sub

#End Region

End Module
