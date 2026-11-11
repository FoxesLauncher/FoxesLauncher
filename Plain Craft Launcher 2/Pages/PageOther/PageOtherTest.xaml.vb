Imports System.Net
Imports System.Runtime.InteropServices
Imports System.Security.Principal

Public Class PageOtherTest

#Region "自定义下载"

    Private IsLoad As Boolean = False

    Private Sub PageOtherTest_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True
        TextDownloadFolder.Text = Settings.Get(Of String)("CacheDownloadFolder")
        TextDownloadFolder.Validate()
        If Not TextDownloadFolder.IsValidated OrElse TextDownloadFolder.Text = "" Then
            TextDownloadFolder.Text = Paths.Base & "PCL\MyDownload\"
        End If
        TextDownloadFolder.Validate()
        RefreshDownloadNameRule()
        TextDownloadUrl.Validate()
        TextDownloadName.Validate()
        StartButtonRefresh()
        AddHandler MyTextBox.ValidateChanged, AddressOf TextBox_ValidateChanged
    End Sub

    Private Sub TextBox_ValidateChanged(sender As Object, e As EventArgs)
        If sender Is TextDownloadUrl OrElse sender Is TextDownloadFolder OrElse sender Is TextDownloadName Then
            StartButtonRefresh()
        End If
    End Sub

    Private Sub RefreshDownloadNameRule()
        Dim Rule = TextDownloadName.ValidateRules.OfType(Of ValidateFileName)().FirstOrDefault()
        If Rule IsNot Nothing Then Rule.ParentFolder = TextDownloadFolder.Text
    End Sub

    Private Sub SaveCacheDownloadFolder()
        Settings.Set("CacheDownloadFolder", TextDownloadFolder.Text)
        RefreshDownloadNameRule()
    End Sub

    Private Sub StartButtonRefresh()
        If BtnDownloadStart Is Nothing Then Return
        BtnDownloadStart.IsEnabled = TextDownloadFolder.IsValidated AndAlso TextDownloadUrl.IsValidated AndAlso TextDownloadName.IsValidated
        BtnDownloadOpen.IsEnabled = TextDownloadFolder.IsValidated
    End Sub

    Private Sub TextDownloadUrl_KeyDown(sender As Object, e As KeyEventArgs) Handles TextDownloadUrl.KeyDown, TextDownloadFolder.KeyDown, TextDownloadName.KeyDown
        If e.Key = Key.Return AndAlso BtnDownloadStart.IsEnabled Then BtnDownloadStart_Click(sender, Nothing)
    End Sub

    Private Sub TextDownloadUrl_TextChanged(sender As Object, e As RoutedEventArgs) Handles TextDownloadUrl.ValidatedTextChanged
        Try
            If TextDownloadName.Text <> "" OrElse TextDownloadUrl.Text = "" Then Return
            TextDownloadName.Text = PathUtils.GetLastPart(WebUtility.UrlDecode(TextDownloadUrl.Text))
        Catch ex As Exception
            Logger.Warn(ex, "自动填充下载文件名失败")
        End Try
    End Sub

    Private Sub TextDownloadFolder_TextChanged(sender As Object, e As RoutedEventArgs) Handles TextDownloadFolder.ValidatedTextChanged
        SaveCacheDownloadFolder()
    End Sub

    Private Sub BtnDownloadStart_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDownloadStart.Click
        StartCustomDownload(TextDownloadUrl.Text, TextDownloadName.Text, TextDownloadFolder.Text)
        TextDownloadUrl.Text = ""
        TextDownloadUrl.Validate()
        TextDownloadUrl.ForceShowAsSuccess()
        TextDownloadName.Text = ""
        TextDownloadName.Validate()
        TextDownloadName.ForceShowAsSuccess()
        StartButtonRefresh()
    End Sub

    Private Sub BtnDownloadSelect_Click(sender As Object, e As EventArgs) Handles BtnDownloadSelect.Click
        Dim Folder = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault
        If Folder IsNot Nothing Then TextDownloadFolder.Text = Folder
    End Sub

    Private Sub BtnDownloadOpen_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDownloadOpen.Click
        Try
            Dim Folder = TextDownloadFolder.Text
            DirectoryUtils.Create(Folder)
            StartProcess(Folder)
        Catch ex As Exception
            Logger.Warn(ex, "打开下载文件夹失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Shared Sub DownloadState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                Hint(Loader.Name & "完成！", HintType.Green)
                Interaction.Beep()
            Case LoadState.Failed
                Logger.Error(Loader.Error, Loader.Name & "失败", LogBehavior.Toast)
                Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Interaction.Beep()
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
        End Select
    End Sub

    ''' <summary>
    ''' 使用 PCL 的下载引擎下载任意文件。
    ''' </summary>
    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Try
            If String.IsNullOrWhiteSpace(FileName) Then FileName = PathUtils.GetLastPart(WebUtility.UrlDecode(Url))
            If String.IsNullOrWhiteSpace(FileName) Then
                Hint("未能确定下载文件名，请手动指定文件名！", HintType.Red)
                Return
            End If
            If String.IsNullOrWhiteSpace(Folder) Then
                Dim SelectedFile = Dialogs.SaveFile("选择文件保存位置", FileName)
                If SelectedFile Is Nothing Then Return
                '用户可能在保存对话框中修改了文件名
                FileName = PathUtils.GetLastPart(SelectedFile)
                Folder = PathUtils.RemoveLastPart(SelectedFile)
            End If
            Folder = Folder.Replace("/", "\").TrimEnd(New Char() {"\"c}) & "\"
            Try
                DirectoryUtils.Create(Folder)
                CheckPermissionWithException(Folder)
            Catch ex As Exception
                Logger.Error(ex, $"访问文件夹失败（{Folder}）", LogBehavior.Alert)
                Return
            End Try
            Logger.Info($"自定义下载文件名：{FileName}")
            Logger.Info($"自定义下载文件目标：{Folder}")
            Dim Uuid As Integer = GetUuid()
            Dim DownloadLoader As New LoaderDownload("自定义下载文件：" & FileName & " ",
                New List(Of NetFile) From {New NetFile({Url}, Folder & FileName, Nothing, True)})
            Dim Loader As New LoaderCombo(Of Integer)("自定义下载 (" & Uuid & ") ",
                New List(Of LoaderBase) From {DownloadLoader}) With {.OnStateChanged = AddressOf DownloadState}
            Loader.Start()
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始自定义下载失败", LogBehavior.Alert)
        End Try
    End Sub

#End Region

#Region "清理游戏垃圾"

    Public Shared Sub RubbishClear()
        RunInUi(
        Sub()
            If FrmOtherTest IsNot Nothing AndAlso FrmOtherTest.BtnClear IsNot Nothing AndAlso FrmOtherTest.BtnClear.IsLoaded Then
                FrmOtherTest.BtnClear.IsEnabled = False
            End If
        End Sub)
        RunInNewThread(
        Sub()
            Try
                If HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading Then
                    Hint("请先关闭所有运行中的游戏！", HintType.Blue)
                ElseIf HasDownloadingTask(False) Then
                    Hint("请在所有下载任务完成后再来清理吧！", HintType.Blue)
                Else
                    If Not McFolderList.Any() Then McFolderListLoader.Start()
                    If PathTemp = Path.GetTempPath() & "PCL\" Then
                        If Settings.Get(Of Integer)("HintClearRubbish") <= 2 Then
                            If MyMsgBox("即将清理游戏日志、错误报告、缓存等文件。" & vbCrLf &
                                        "虽然应该没人往这些地方放重要文件，但还是问一下，是否确认继续？" & vbCrLf & vbCrLf &
                                        "在完成清理后，需要重启 PCL。", "清理确认", "确定", "取消", "", False, True, True) = 2 Then Return
                            Settings.Set("HintClearRubbish", Settings.Get(Of Integer)("HintClearRubbish") + 1)
                        End If
                    ElseIf MyMsgBox("即将清理游戏日志、错误报告、缓存等文件。" & vbCrLf & vbCrLf &
                                    "你已将缓存文件夹手动修改为：" & PathTemp & vbCrLf &
                                    "清理过程中，将删除该文件夹中的所有内容，且无法恢复。请确认其中没有除了 PCL 缓存以外的重要文件！" & vbCrLf & vbCrLf &
                                    "在完成清理后，需要重启 PCL。", "清理确认", "确定", "取消", "", False, True, True) = 2 Then
                        Return
                    End If
                    Dim FolderList As New List(Of String)
                    If Not McFolderList.Any() Then McFolderListLoader.WaitForExit()
                    For Each FolderInfo In McFolderList
                        FolderList.Add(FolderInfo.Location)
                        FolderList.AddRange(DirectoryUtils.EnumerateDirectories(FolderInfo.Location & "versions", False, "*"))
                    Next
                    Dim ClearFolder As Action(Of String) =
                        Sub(Dir As String)
                            Try
                                DirectoryUtils.Delete(Dir, False)
                            Catch ex As Exception
                                Logger.Warn(ex, $"清理文件夹失败（{Dir}）", LogBehavior.None)
                            End Try
                        End Sub
                    For Each Folder In FolderList
                        ClearFolder(Path.Combine(Folder, ".mixin.out"))
                        ClearFolder(Path.Combine(Folder, "crash-reports"))
                        ClearFolder(Path.Combine(Folder, "debug"))
                        ClearFolder(Path.Combine(Folder, "logs"))
                        For Each FilePath In DirectoryUtils.EnumerateFiles(Folder, False, "*")
                            Dim LastPart = PathUtils.GetLastPart(FilePath)
                            If LastPart.StartsWithF("hs_err_pid", True) OrElse LastPart.EndsWithF(".log", True) OrElse LastPart = "WailaErrorOutput.txt" Then
                                FileUtils.Delete(FilePath)
                            End If
                        Next
                        For Each SubFolder In DirectoryUtils.EnumerateDirectories(Folder, False, "*")
                            Dim LastPart = PathUtils.GetLastPart(SubFolder)
                            If LastPart = PathUtils.GetLastPart(Folder) & "-natives" OrElse LastPart = "natives-windows-x86_64" Then
                                ClearFolder(SubFolder)
                            End If
                        Next
                    Next
                    ClearFolder(PathTemp)
                    ClearFolder(OsDrive & "ProgramData\PCL\")
                    If MyMsgBox("垃圾已经清理好啦！", "清理完成", "关闭 PCL", "重启 PCL", "", False, True, True) = 2 Then
                        StartProcess(New ProcessStartInfo(PathExe, "--wait"))
                    End If
                    FormMain.EndProgramForce(ProcessReturnValues.Success)
                End If
            Catch ex As Exception
                Logger.Error(ex, "清理垃圾失败", LogBehavior.Alert)
            Finally
                RunInUiWait(
                Sub()
                    If FrmOtherTest IsNot Nothing AndAlso FrmOtherTest.BtnClear IsNot Nothing AndAlso FrmOtherTest.BtnClear.IsLoaded Then
                        FrmOtherTest.BtnClear.IsEnabled = True
                    End If
                End Sub)
            End Try
        End Sub, "Rubbish Clear", ThreadPriority.Normal)
    End Sub

#End Region

#Region "内存优化"

    Private Shared IsMemoryOptimizeRunning As Boolean = False

    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If IsMemoryOptimizeRunning Then
            If ShowHint Then Hint("内存优化尚未结束，请稍等！", HintType.Blue)
            Return
        End If
        IsMemoryOptimizeRunning = True
        Try
            If ShowHint Then Hint("正在进行内存优化……", HintType.Blue)
            Dim MemoryBefore As Long
            Dim MemoryDelta As Long
            If WindowsUtils.HasAdminRole() Then
                MemoryBefore = CLng(My.Computer.Info.AvailablePhysicalMemory)
                MemoryOptimizeInternal(ShowHint)
                MemoryDelta = CLng(My.Computer.Info.AvailablePhysicalMemory) - MemoryBefore
            Else
                Logger.Info("没有管理员权限，将以命令行方式进行内存优化")
                Try
                    MemoryDelta = CLng(RunAsAdmin("--memory")) * 1024L
                Catch ex As Exception
                    Logger.Warn(ex, "命令行形式内存优化失败", LogBehavior.Toast)
                    If ShowHint Then Hint("获取管理员权限失败，请尝试右键 PCL，选择 [以管理员身份运行]！", HintType.Red)
                    Return
                End Try
                If MemoryDelta < 0 Then Return
            End If
            Dim MemoryNow As String = StringUtils.FormatByteSize(CLng(My.Computer.Info.AvailablePhysicalMemory))
            Logger.Info($"内存优化完成，可用内存改变量：{StringUtils.FormatByteSize(MemoryDelta)}，大致剩余内存：{MemoryNow}")
            If MemoryDelta > 0 Then
                If ShowHint Then Hint($"内存优化完成，可用内存增加了 {StringUtils.FormatByteSize(CLng(Math.Round(MemoryDelta * 0.8)))}，目前剩余内存 {MemoryNow}！", HintType.Green)
            ElseIf ShowHint Then
                Hint($"内存优化完成，已经优化到了最佳状态，目前剩余内存 {MemoryNow}！", HintType.Blue)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "内存优化失败", If(ShowHint, LogBehavior.Toast, LogBehavior.None))
            If ShowHint Then Hint("内存优化失败：" & ex.Message, HintType.Red)
        Finally
            IsMemoryOptimizeRunning = False
        End Try
    End Sub

    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If Not WindowsUtils.HasAdminRole() Then
            Throw New Exception("内存优化功能需要管理员权限！" & vbCrLf &
                                "如果需要自动以管理员身份启动 PCL，可以右键 PCL，打开 属性 → 兼容性 → 以管理员身份运行此程序。")
        End If
        Logger.Info("获取内存优化权限")
        Using Identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query Or TokenAccessLevels.AdjustPrivileges)
            Dim Token As New PrivilegeToken With {.PrivilegeCount = 1, .Luid = 0, .Attributes = 2}
            Dim SystemName As String = Nothing
            Dim PrivilegeName As String = "SeProfileSingleProcessPrivilege"
            If Not LookupPrivilegeValue(SystemName, PrivilegeName, Token.Luid) Then
                Throw New Exception($"获取内存优化权限失败（错误代码：{Marshal.GetLastWin32Error()}）")
            End If
            Dim PreviousState As IntPtr = IntPtr.Zero
            Dim ReturnLength As Integer = 0
            If Not AdjustTokenPrivileges(Identity.Token, False, Token, Marshal.SizeOf(Token), PreviousState, ReturnLength) OrElse
               Marshal.GetLastWin32Error() <> 0 Then
                Throw New Exception($"获取内存优化权限失败（错误代码：{Marshal.GetLastWin32Error()}）")
            End If
        End Using
        For Level As Integer = 2 To 4
            Logger.Info($"内存优化操作 {Level} 开始")
            Dim Handle = GCHandle.Alloc(Level, GCHandleType.Pinned)
            Dim Result As UInteger
            Try
                Result = NtSetSystemInformation(80, Handle.AddrOfPinnedObject(), Marshal.SizeOf(Level))
            Finally
                Handle.Free()
            End Try
            If Result <> 0 Then Throw New Exception($"内存优化操作 {Level} 失败（错误代码：{Result}）")
        Next
    End Sub

    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Private Structure PrivilegeToken
        Public PrivilegeCount As Integer
        Public Luid As Long
        Public Attributes As Integer
    End Structure

    <DllImport("ntdll.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function NtSetSystemInformation(SystemInformationClass As Integer, SystemInformation As IntPtr, SystemInformationLength As Integer) As UInteger
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Ansi, EntryPoint:="LookupPrivilegeValueA", ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function LookupPrivilegeValue(<MarshalAs(UnmanagedType.VBByRefStr)> ByRef lpSystemName As String,
                                                <MarshalAs(UnmanagedType.VBByRefStr)> ByRef lpName As String,
                                                ByRef lpLuid As Long) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function AdjustTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean,
                                                  ByRef NewState As PrivilegeToken, BufferLength As Integer,
                                                  ByRef PreviousState As IntPtr, ByRef ReturnLength As Integer) As Boolean
    End Function

#End Region

#Region "占位功能"

    Public Shared Sub Jrrp()
        Hint("为便于维护，开源内容中不包含今日人品功能……")
    End Sub

    Public Shared Function GetRandomCave() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function

#End Region

End Class
