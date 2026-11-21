Imports System.Windows.Markup

''' <summary>
''' 允许 XAML 直接使用完整句子作为语言键。
''' DynamicResource 的键不能包含逗号、花括号或换行，因此这类长 ToolTip
''' 改用 {local:I18n '完整原文'}，避免退回到“文件名 + 属性 + 截断原文”的旧键。
''' </summary>
Public Class I18nExtension
    Inherits MarkupExtension

    Public Property Text As String

    Public Sub New()
    End Sub

    Public Sub New(Text As String)
        Me.Text = Text
    End Sub

    Public Overrides Function ProvideValue(serviceProvider As IServiceProvider) As Object
        Return ModLanguage.T(Text)
    End Function

End Class
