''' <summary>
''' 区域与语言设置。
''' 区域用于决定部分功能的可用性限制，语言目前仅作为配置记录，尚未接入 i18n 框架。
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
    Public ReadOnly RegionEntries As New List(Of RegionLanguageEntry) From {
        New RegionLanguageEntry(RegionAsiaMainlandChina, "亚洲（中国大陆）"),
        New RegionLanguageEntry(RegionAsiaHongKongMacaoTaiwan, "亚洲（中国香港、中国澳门、中国台湾）"),
        New RegionLanguageEntry(RegionAsiaOther, "亚洲（其他）"),
        New RegionLanguageEntry(RegionAfrica, "非洲"),
        New RegionLanguageEntry(RegionAmericas, "美洲"),
        New RegionLanguageEntry(RegionEurope, "欧洲"),
        New RegionLanguageEntry(RegionOceania, "大洋洲"),
        New RegionLanguageEntry(RegionOther, "其他")
    }

    ''' <summary>
    ''' 可选的语言列表。
    ''' 目前仅实现语言选择的保存逻辑，暂未接入 i18n 框架。
    ''' </summary>
    Public ReadOnly LanguageEntries As New List(Of RegionLanguageEntry) From {
        New RegionLanguageEntry(LanguageSimplifiedChineseMainland, "简体中文（中国大陆）")
    }

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
            Dim Value As String = Settings.Get(Of String)("SystemLanguage")
            If LanguageEntries.Any(Function(e) e.Code = Value) Then Return Value
            Return LanguageSimplifiedChineseMainland
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
    Public Const GameDownloadRestrictedMessage As String = "为遵守相关法律法规，FXL 在当前区域不提供 Minecraft 下载与安装功能，非常抱歉 qwq…"

    ''' <summary>
    ''' 离线账户限制的提示文本。
    ''' </summary>
    Public Const OfflineAccountRestrictedMessage As String = "根据你选择的区域，必须先登录正版（微软）账号，才能使用离线账户。"

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
        If MyMsgBox("区域或语言设置已更改，请重启启动器。", "提示", "稍后", "重启启动器") = 2 Then
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
    ''' </summary>
    Public Sub PromptRegionLanguageIfNeeded()
        If HasRegionLanguageSetting() Then Return
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
    End Sub

End Module

''' <summary>
''' 区域与语言选择弹窗的结果。
''' </summary>
Public Class RegionLanguageSelection
    Public Region As String
    Public Language As String
End Class
