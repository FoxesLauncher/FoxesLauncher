Imports System.Runtime.InteropServices

''' <summary>
''' 在启动前把 Minecraft 使用的 FreeType 替换为已修复的构建，用于防御 Minecraft2Shell 一类漏洞。
''' </summary>
Public Module ModFreeTypePatch

    Private Const PatchedVersion As String = "2.15.0"

    '官方修补版 natives（FreeType 2.15.0），直接复用 Mojang 的构件，避免自行编译
    Private Const JarUrlX64 As String = "https://libraries.minecraft.net/org/lwjgl/lwjgl-freetype/3.4.3-mojang-2.15.0/lwjgl-freetype-3.4.3-mojang-2.15.0-natives-windows.jar"
    Private Const JarSizeX64 As Long = 1190826
    Private Const JarSha1X64 As String = "f85d0f5acaa594f9e93d657f8feff85d6a427b6a"
    Private Const DllSha1X64 As String = "5733dfe12c134d452749c7e34f83b67cf0363179"

    Private Const JarUrlArm64 As String = "https://libraries.minecraft.net/org/lwjgl/lwjgl-freetype/3.4.3-mojang-2.15.0/lwjgl-freetype-3.4.3-mojang-2.15.0-natives-windows-arm64.jar"
    Private Const JarSizeArm64 As Long = 1043884
    Private Const JarSha1Arm64 As String = "5f475c76490bb368f235017985d8d9dc6b5b5121"
    Private Const DllSha1Arm64 As String = "cc92c900a606cac88ed143e2226e4ef24d63de09"

    ''' <summary>
    ''' 检查并替换实例 natives 文件夹中的 freetype.dll。失败时只记录日志，不影响启动。
    ''' </summary>
    Public Sub FreeTypePatchApply(NativesFolder As String, Loader As LoaderBase)
        Try
            If String.IsNullOrWhiteSpace(NativesFolder) Then Return
            If Not Settings.Get(Of Boolean)("LaunchAdvanceFontGuardPatch") Then Return

            Dim Target As String = FreeTypeFindTarget(NativesFolder)
            If Target = "" Then
                McLaunchLog("未找到 natives 中的 freetype.dll，跳过 FreeType 检查")
                Return
            End If

            Dim Current As Version = Nothing
            If FreeTypeProbeVersion(Target, Current) AndAlso Current >= New Version(2, 15, 0) Then
                McLaunchLog($"当前 FreeType 版本为 {Current}，无需替换")
                Return
            End If

            Dim Arch As String = FreeTypeGetArch(Target)
            If Arch = "" Then
                McLaunchLog("当前运行架构不受支持，跳过 FreeType 替换", LogLevel.Warn)
                Return
            End If

            Dim Patched As String = FreeTypeEnsureDll(Arch, Loader)
            If Patched = "" Then
                McLaunchLog("无法获取已修复的 FreeType 库，已跳过自动替换", LogLevel.Warn)
                Return
            End If

            FileUtils.Copy(Patched, Target)
            McLaunchLog($"已将 FreeType 替换为 {PatchedVersion}（原版本 {If(Current, "未知")}）：{Target}")
        Catch ex As Exception
            If ex.IsCanceled Then Throw
            Logger.Warn(ex, "替换 FreeType 库失败，游戏仍会继续启动")
        End Try
    End Sub

    Private Function FreeTypeFindTarget(NativesFolder As String) As String
        Try
            Dim Candidates = DirectoryUtils.EnumerateFiles(NativesFolder, includeSubDirectories:=True, searchPattern:="freetype.dll").ToList()
            If Not Candidates.Any() Then Return ""
            Dim Preferred = Candidates.FirstOrDefault(
                Function(f) f.Replace("/"c, "\"c).ToLowerInvariant().Contains("org\lwjgl\freetype"))
            Return If(Preferred, Candidates.First())
        Catch ex As Exception
            Logger.Warn(ex, "查找 freetype.dll 失败")
            Return ""
        End Try
    End Function

    Private Function FreeTypeGetArch(Target As String) As String
        Dim Path As String = Target.Replace("/"c, "\"c).ToLowerInvariant()
        If Path.Contains("\arm64\") Then Return "arm64"
        If Path.Contains("\x86\") Then Return "" '官方修补版没有 32 位构建
        If Path.Contains("\x64\") Then Return "x64"
        Return "x64"
    End Function

    Private Function FreeTypeEnsureDll(Arch As String, Loader As LoaderBase) As String
        Dim Folder As String = PathTemp & "Cache\FontGuard\"
        DirectoryUtils.Create(Folder)
        Dim DllPath As String = Folder & "freetype-" & PatchedVersion & "-" & Arch & ".dll"
        Dim ExpectedDll As String = FreeTypeDllSha1(Arch)
        Dim ExpectedJar As String = FreeTypeJarSha1(Arch)
        Dim Url As String = FreeTypeJarUrl(Arch)
        If ExpectedDll = "" OrElse ExpectedJar = "" OrElse Url = "" Then Return ""

        If FileUtils.Exists(DllPath) AndAlso FreeTypeHashMatches(DllPath, ExpectedDll) Then Return DllPath

        Dim JarPath As String = Folder & "lwjgl-freetype-" & PatchedVersion & "-" & Arch & ".jar"
        Try
            If Not FileUtils.Exists(JarPath) OrElse Not FreeTypeHashMatches(JarPath, ExpectedJar) Then
                McLaunchLog($"正在下载已修复的 FreeType 库（{Arch}）")
                NetDownloadByLoader({Url}, JarPath, Loader,
                    New FileChecker With {.ActualSize = FreeTypeJarSize(Arch), .Hash = ExpectedJar})
            End If
        Catch ex As Exception
            If ex.IsCanceled Then Throw
            Logger.Warn(ex, "下载已修复的 FreeType 库失败")
            Return ""
        End Try

        Try
            Dim Data As Byte() = Nothing
            Using Zip = FileUtils.OpenZip(JarPath)
                Dim Entry = Zip.Entries.FirstOrDefault(
                    Function(e) e.FullName.Replace("/"c, "\"c).EndsWith("\org\lwjgl\freetype\freetype.dll", StringComparison.OrdinalIgnoreCase))
                If Entry Is Nothing Then
                    Logger.Warn("已修复的 FreeType 库中未找到 freetype.dll")
                    Return ""
                End If
                Using Stream = Entry.Open()
                    Using Memory As New MemoryStream()
                        Stream.CopyTo(Memory)
                        Data = Memory.ToArray()
                    End Using
                End Using
            End Using
            If Data Is Nothing OrElse Data.Length = 0 Then Return ""
            FileUtils.Write(DllPath, Data)
            If Not FreeTypeHashMatches(DllPath, ExpectedDll) Then
                Logger.Warn("已修复的 FreeType 库校验失败，已删除")
                FileUtils.Delete(DllPath)
                Return ""
            End If
            Return DllPath
        Catch ex As Exception
            Logger.Warn(ex, "解压已修复的 FreeType 库失败")
            Return ""
        End Try
    End Function

    Private Function FreeTypeJarUrl(Arch As String) As String
        Select Case Arch
            Case "x64"
                Return JarUrlX64
            Case "arm64"
                Return JarUrlArm64
            Case Else
                Return ""
        End Select
    End Function

    Private Function FreeTypeJarSize(Arch As String) As Long
        Select Case Arch
            Case "x64"
                Return JarSizeX64
            Case "arm64"
                Return JarSizeArm64
            Case Else
                Return -1
        End Select
    End Function

    Private Function FreeTypeJarSha1(Arch As String) As String
        Select Case Arch
            Case "x64"
                Return JarSha1X64
            Case "arm64"
                Return JarSha1Arm64
            Case Else
                Return ""
        End Select
    End Function

    Private Function FreeTypeDllSha1(Arch As String) As String
        Select Case Arch
            Case "x64"
                Return DllSha1X64
            Case "arm64"
                Return DllSha1Arm64
            Case Else
                Return ""
        End Select
    End Function

    Private Function FreeTypeHashMatches(Path As String, Expected As String) As Boolean
        Try
            If String.IsNullOrEmpty(Expected) OrElse Not FileUtils.Exists(Path) Then Return False
            Return CryptographyUtils.ComputeFileHash(Path, CryptographyUtils.HashMethod.Sha1).Equals(Expected, StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 读取 DLL 的 FreeType 版本。会先复制到临时文件，避免与已加载的同名模块冲突。
    ''' </summary>
    Private Function FreeTypeProbeVersion(Path As String, ByRef Result As Version) As Boolean
        Dim ProbePath As String = PathTemp & "Cache\FontGuard\probe-" & Guid.NewGuid().ToString("N") & ".dll"
        Try
            DirectoryUtils.Create(PathUtils.RemoveLastPart(ProbePath))
            FileUtils.Copy(Path, ProbePath)
            Dim Handle As IntPtr = NativeMethods.LoadLibraryW(ProbePath)
            If Handle = IntPtr.Zero Then Return False
            Try
                Dim InitAddress As IntPtr = NativeMethods.GetProcAddress(Handle, "FT_Init_FreeType")
                Dim VersionAddress As IntPtr = NativeMethods.GetProcAddress(Handle, "FT_Library_Version")
                Dim DoneAddress As IntPtr = NativeMethods.GetProcAddress(Handle, "FT_Done_FreeType")
                If InitAddress = IntPtr.Zero OrElse VersionAddress = IntPtr.Zero Then Return False

                Dim Init As NativeMethods.InitDelegate = Marshal.GetDelegateForFunctionPointer(Of NativeMethods.InitDelegate)(InitAddress)
                Dim GetVersion As NativeMethods.VersionDelegate = Marshal.GetDelegateForFunctionPointer(Of NativeMethods.VersionDelegate)(VersionAddress)
                Dim Done As NativeMethods.DoneDelegate = Nothing
                If DoneAddress <> IntPtr.Zero Then Done = Marshal.GetDelegateForFunctionPointer(Of NativeMethods.DoneDelegate)(DoneAddress)

                Dim Library As IntPtr = IntPtr.Zero
                If Init(Library) <> 0 Then Return False
                Dim Major As Integer = 0, Minor As Integer = 0, Patch As Integer = 0
                GetVersion(Library, Major, Minor, Patch)
                If Done IsNot Nothing Then Done(Library)
                Result = New Version(Major, Minor, Patch)
                Return True
            Finally
                NativeMethods.FreeLibrary(Handle)
            End Try
        Catch ex As Exception
            Logger.Trace($"读取 FreeType 版本失败：{Path}，{ex.Message}")
            Return False
        Finally
            Try
                FileUtils.Delete(ProbePath)
            Catch
            End Try
        End Try
    End Function

    Private NotInheritable Class NativeMethods
        <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function LoadLibraryW(LibFileName As String) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function FreeLibrary(ModuleHandle As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", CharSet:=CharSet.Ansi, SetLastError:=True)>
        Public Shared Function GetProcAddress(ModuleHandle As IntPtr, ProcName As String) As IntPtr
        End Function

        Public Delegate Function InitDelegate(ByRef Library As IntPtr) As Integer
        Public Delegate Sub VersionDelegate(Library As IntPtr, ByRef Major As Integer, ByRef Minor As Integer, ByRef Patch As Integer)
        Public Delegate Function DoneDelegate(Library As IntPtr) As Integer
    End Class

End Module
