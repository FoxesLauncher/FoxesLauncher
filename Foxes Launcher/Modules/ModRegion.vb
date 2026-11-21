''' <summary>
''' 区域与语言设置。
''' 区域用于决定部分功能的可用性限制，语言用于选择启动器界面语言包。
''' </summary>
Public Module ModRegion

    '区域代码
    Public Const RegionAsiaMainlandChina As String = "asia_cn"
    Public Const RegionAsiaHongKongMacaoTaiwan As String = "asia_hk_mo_tw"
    Public Const RegionAsiaOther As String = "asia_other"
    Public Const RegionAfrica As String = "africa"
    Public Const RegionAmericas As String = "americas"
    Public Const RegionEurope As String = "europe"
    Public Const RegionOceania As String = "oceania"
    Public Const RegionOther As String = "other"

    '语言代码
    Public Const LanguageSimplifiedChineseMainland As String = "zh_cn"

    ''' <summary>
    ''' 一条区域或语言的显示名称与代码。
    ''' </summary>
    Public Structure RegionLanguageEntry
        Public Code As String
        Public Name As String
        Public Sub New(Code As String, Name As String)
            Me.Code = Code
            Me.Name = Name
        End Sub
    End Structure

    ''' <summary>
    ''' 可选的区域列表。
    ''' </summary>
    Public ReadOnly Property RegionEntries As List(Of RegionLanguageEntry)
        Get
            Return New List(Of RegionLanguageEntry) From {
                New RegionLanguageEntry(RegionAsiaMainlandChina, T("亚洲（中国大陆）")),
                New RegionLanguageEntry(RegionAsiaHongKongMacaoTaiwan, T("亚洲（中国香港、中国澳门、中国台湾）")),
                New RegionLanguageEntry(RegionAsiaOther, T("亚洲（其他）")),
                New RegionLanguageEntry(RegionAfrica, T("非洲")),
                New RegionLanguageEntry(RegionAmericas, T("美洲")),
                New RegionLanguageEntry(RegionEurope, T("欧洲")),
                New RegionLanguageEntry(RegionOceania, T("大洋洲")),
                New RegionLanguageEntry(RegionOther, T("其他"))
            }
        End Get
    End Property

    ''' <summary>
    ''' 可选的语言列表。
    ''' 语言包存放于 FXL\Languages 或内嵌在程序中，新增语言包后会自动出现在此列表中。
    ''' </summary>
    Public ReadOnly Property LanguageEntries As List(Of RegionLanguageEntry)
        Get
            Dim Result As New List(Of RegionLanguageEntry)
            For Each Code In ModLanguage.AvailableLanguages
                Result.Add(New RegionLanguageEntry(Code, LanguageDisplayName(Code)))
            Next
            If Result.Count = 0 Then Result.Add(New RegionLanguageEntry(LanguageSimplifiedChineseMainland, LanguageDisplayName(LanguageSimplifiedChineseMainland)))
            Return Result
        End Get
    End Property

    ''' <summary>
    ''' 获取语言在界面上的显示名称。显示名来自语言清单，避免翻译后语言名互相看不懂。
    ''' </summary>
    Public Function LanguageDisplayName(Code As String) As String
        Return ModLanguage.GetDisplayName(Code)
    End Function

    ''' <summary>
    ''' 当前选择的区域代码。若配置无效，则回退到亚洲（中国大陆）。
    ''' </summary>
    Public ReadOnly Property CurrentRegion As String
        Get
            Dim Value As String = Settings.Get(Of String)("SystemRegion")
            If RegionEntries.Any(Function(e) e.Code = Value) Then Return Value
            Return RegionAsiaMainlandChina
        End Get
    End Property

    ''' <summary>
    ''' 当前选择的语言代码。若配置无效，则回退到简体中文（中国大陆）。
    ''' </summary>
    Public ReadOnly Property CurrentLanguage As String
        Get
            Return ModLanguage.CurrentLanguage
        End Get
    End Property

    ''' <summary>
    ''' 是否限制 Minecraft 的下载与安装（亚洲（中国大陆））。
    ''' 限制时仅显示提示，不隐藏入口。
    ''' </summary>
    Public ReadOnly Property IsGameDownloadRestricted As Boolean
        Get
            Return CurrentRegion = RegionAsiaMainlandChina
        End Get
    End Property

    ''' <summary>
    ''' 是否要求先登录微软账户才能使用离线账户。
    ''' </summary>
    Public ReadOnly Property IsOfflineAccountRestricted As Boolean
        Get
            Select Case CurrentRegion
                Case RegionAsiaOther, RegionAfrica, RegionAmericas, RegionEurope, RegionOceania
                    Return True
                Case Else
                    Return False
            End Select
        End Get
    End Property

    ''' <summary>
    ''' 区域限制的提示文本。
    ''' </summary>
    Public ReadOnly Property GameDownloadRestrictedMessage As String
        Get
            Return T("为遵守相关法律法规，FXL 在当前区域不提供 Minecraft 下载与安装功能，非常抱歉 qwq…")
        End Get
    End Property

    ''' <summary>
    ''' 离线账户限制的提示文本。
    ''' </summary>
    Public ReadOnly Property OfflineAccountRestrictedMessage As String
        Get
            Return T("根据你选择的区域，必须先登录正版（微软）账号，才能使用离线账户。")
        End Get
    End Property

    ''' <summary>
    ''' 获取设置文件中是否已经保存了有效的区域与语言。
    ''' </summary>
    Public Function HasRegionLanguageSetting() As Boolean
        Return Not String.IsNullOrWhiteSpace(ReadIni("Setup", "SystemRegion")) AndAlso
               Not String.IsNullOrWhiteSpace(ReadIni("Setup", "SystemLanguage"))
    End Function

    ''' <summary>
    ''' 设置中区域或语言被更改时，提示需要重启启动器。
    ''' </summary>
    Public Sub NotifyRegionLanguageChanged()
        If IsApplyingInitialRegionLanguage Then Return
        ShowRestartPrompt()
    End Sub

    ''' <summary>
    ''' 语言设置变更时调用。首次启动写入设置时跳过，避免重复加载语言。
    ''' </summary>
    Public Sub OnLanguageSettingChanged(Language As String)
        If IsApplyingInitialRegionLanguage Then Return
        ApplyLanguageSetting(Language)
    End Sub

    ''' <summary>
    ''' 立即应用新的语言设置，使界面文本（包括随后的重启提醒）使用新语言。
    ''' 若在后台线程调用，会切换到 UI 线程后再更新语言资源。
    ''' </summary>
    Public Sub ApplyLanguageSetting(Language As String)
        Try
            If Application.Current IsNot Nothing AndAlso Not Application.Current.Dispatcher.CheckAccess() Then
                RunInUiWait(Sub() ModLanguage.LoadLanguage(Language))
            Else
                ModLanguage.LoadLanguage(Language)
            End If
        Catch ex As Exception
            Logger.Error(ex, "应用界面语言失败", LogBehavior.Toast)
        End Try
        NotifyRegionLanguageChanged()
    End Sub

    ''' <summary>
    ''' 提示需要重启启动器才能完全应用区域与语言设置。
    ''' 提示文本使用当前已经应用的语言；若在后台线程调用，会切换到 UI 线程显示。
    ''' </summary>
    Private Sub ShowRestartPrompt()
        If Application.Current IsNot Nothing AndAlso Not Application.Current.Dispatcher.CheckAccess() Then
            RunInUiWait(AddressOf ShowRestartPrompt)
            Return
        End If
        If MyMsgBox(T("区域或语言设置已更改，请重启启动器。"), T("提示"), T("稍后"), T("重启启动器")) = 2 Then
            Try
                StartProcess(New ProcessStartInfo(PathExe, "--wait"))
            Catch ex As Exception
                Logger.Error(ex, "重启启动器失败", LogBehavior.Alert)
                Return
            End Try
            FormMain.EndProgramForce(ProcessReturnValues.Success)
        End If
    End Sub

    ''' <summary>
    ''' 首次启动写入区域与语言时使用，避免弹出重启提示。
    ''' </summary>
    Private IsApplyingInitialRegionLanguage As Boolean

    ''' <summary>
    ''' 在首次启动时询问区域与语言。
    ''' 仅在 Setup.ini 中没有有效的区域与语言设置时显示。
    ''' 若选择的语言与启动时使用的语言不同，会立即应用并提示重启启动器。
    ''' </summary>
    Public Sub PromptRegionLanguageIfNeeded()
        If HasRegionLanguageSetting() Then Return
        Dim StartupLanguage As String = ModLanguage.CurrentLanguage
        Dim Result As RegionLanguageSelection = MyMsgBoxRegionLanguage()
        If Result Is Nothing Then
            Result = New RegionLanguageSelection With {.Region = CurrentRegion, .Language = CurrentLanguage}
        End If
        IsApplyingInitialRegionLanguage = True
        Try
            Settings.Set("SystemRegion", Result.Region)
            Settings.Set("SystemLanguage", Result.Language)
            '即使是默认值也要写入文件，避免下次启动时重复询问
            WriteIni("Setup", "SystemRegion", Result.Region)
            WriteIni("Setup", "SystemLanguage", Result.Language)
        Finally
            IsApplyingInitialRegionLanguage = False
        End Try
        Logger.Info($"已保存区域与语言设置：{Result.Region} / {Result.Language}")
        If Result.Language <> StartupLanguage Then
            '弹窗中可能已经预览过新语言，这里再确保一次，让重启提醒使用新语言
            Try
                RunInUiWait(Sub() ModLanguage.LoadLanguage(Result.Language))
            Catch ex As Exception
                Logger.Error(ex, "应用首次启动选择的语言失败", LogBehavior.Toast)
            End Try
            ShowRestartPrompt()
        End If
    End Sub

End Module

''' <summary>
''' 区域与语言选择弹窗的结果。
''' </summary>
Public Class RegionLanguageSelection
    Public Region As String
    Public Language As String
End Class
