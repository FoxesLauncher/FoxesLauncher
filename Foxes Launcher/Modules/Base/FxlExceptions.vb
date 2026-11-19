''' <summary>
''' 磁盘空间不足。用于在下载重试逻辑中按类型判断，而不是匹配已翻译的消息。
''' </summary>
Public Class DiskFullException
    Inherits Exception

    Public Sub New(Message As String)
        MyBase.New(Message)
    End Sub

End Class

''' <summary>
''' 用户未设置自定义皮肤。用于按类型跳过皮肤加载，而不是匹配已翻译的消息。
''' </summary>
Public Class SkinNotSetException
    Inherits Exception

    Public Sub New(Message As String)
        MyBase.New(Message)
    End Sub

    Public Sub New(Message As String, InnerException As Exception)
        MyBase.New(Message, InnerException)
    End Sub

End Class

''' <summary>
''' 可以直接作为简要提示显示给用户的异常，不再依赖 "$" 消息前缀约定。
''' </summary>
Public Class BriefException
    Inherits Exception

    Public Sub New(Message As String)
        MyBase.New(Message)
    End Sub

    Public Sub New(Message As String, InnerException As Exception)
        MyBase.New(Message, InnerException)
    End Sub

End Class

Public Module FxlExceptionUtils

    Public Function IsDiskFullException(Ex As Exception) As Boolean
        Return ContainsException(Of DiskFullException)(Ex)
    End Function

    Public Function IsSkinNotSetException(Ex As Exception) As Boolean
        Return ContainsException(Of SkinNotSetException)(Ex)
    End Function

    Public Function IsBriefException(Ex As Exception) As Boolean
        Return ContainsException(Of BriefException)(Ex)
    End Function

    Private Function ContainsException(Of T As Exception)(Ex As Exception) As Boolean
        Dim Current As Exception = Ex
        While Current IsNot Nothing
            If TypeOf Current Is T Then Return True
            Current = Current.InnerException
        End While
        Return False
    End Function

End Module

