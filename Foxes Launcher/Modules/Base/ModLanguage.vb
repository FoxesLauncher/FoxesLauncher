Imports System.Globalization
Imports System.Reflection
Imports System.Windows.Markup

''' <summary>
''' 启动器多语言支持。
''' 语言包使用 JSON，键为基础语言（zh_cn）的完整原文，值为对应语言的文本。
''' 基础语言包必须完整；其它语言允许缺键，缺键时逐键回退到基础语言包。
''' 切换语言后需要重启启动器才能完全生效。
''' </summary>
Public Module ModLanguage

    ''' <summary>
    ''' 默认语言（基础语言）代码。
    ''' </summary>
    Public Const DefaultLanguage As String = "zh_cn"

    ''' <summary>
    ''' 语言包内嵌资源名称的固定部分。
    ''' </summary>
    Private Const ResourceMarker As String = ".Languages."

    ''' <summary>
    ''' 语言清单文件的固定名称。
    ''' </summary>
    Private Const ManifestFileName As String = "manifest.json"

    Private ReadOnly LanguageData As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly MissingKeys As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly LanguageDisplayNames As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly AvailableLanguageCache As New List(Of String)
    Private LanguageDictionary As ResourceDictionary = Nothing
    Private _CurrentLanguage As String = DefaultLanguage
    Private _IsInitialized As Boolean = False
    Private _IsLanguageMetadataApplied As Boolean = False

    ''' <summary>
    ''' 当前启动器语言代码，例如 zh_cn。
    ''' </summary>
    Public ReadOnly Property CurrentLanguage As String
        Get
            Return _CurrentLanguage
        End Get
    End Property

    ''' <summary>
    ''' 当前是否使用默认语言（简体中文）。
    ''' 部分来自外部的单语言资源仅对简体中文用户可见。
    ''' </summary>
    Public ReadOnly Property IsDefaultLanguage As Boolean
        Get
            Return CurrentLanguage = DefaultLanguage
        End Get
    End Property

    ''' <summary>
    ''' 是否已经加载过语言包。
    ''' </summary>
    Public ReadOnly Property IsInitialized As Boolean
        Get
            Return _IsInitialized
        End Get
    End Property

    ''' <summary>
    ''' 当前可用的语言代码列表。
    ''' </summary>
    Public ReadOnly Property AvailableLanguages As IReadOnlyList(Of String)
        Get
            Return AvailableLanguageCache
        End Get
    End Property

    ''' <summary>
    ''' 初始化语言包。应在创建任何窗口或页面之前调用。
    ''' </summary>
    Public Sub Initialize()
        If _IsInitialized Then Return
        Dim Language As String = DefaultLanguage
        Try
            '直接读取配置文件，避免在语言包加载前触发 Settings 的静态初始化
            Language = NormalizeLanguage(ReadIni("Setup", "SystemLanguage"))
        Catch ex As Exception
            '配置文件尚未就绪时忽略，直接使用默认语言
        End Try
        If String.IsNullOrEmpty(Language) Then Language = DefaultLanguage
        LoadLanguage(Language)
        _IsInitialized = True
    End Sub

    ''' <summary>
    ''' 重新加载指定语言。切换语言后仍建议重启启动器，以刷新代码中直接赋值的文本。
    ''' </summary>
    Public Sub LoadLanguage(Language As String)
        Language = NormalizeLanguage(Language)
        RefreshAvailableLanguages()
        If Not AvailableLanguageCache.Contains(Language) Then Language = DefaultLanguage

        LanguageData.Clear()
        MissingKeys.Clear()

        '先加载基础语言，再叠加目标语言，实现缺键回退
        LoadEmbeddedLanguage(DefaultLanguage)
        LoadExternalLanguage(DefaultLanguage)
        If Language <> DefaultLanguage Then
            LoadEmbeddedLanguage(Language)
            LoadExternalLanguage(Language)
        End If

        If LanguageData.Count = 0 Then
            Logger.Error($"基础语言包 {DefaultLanguage}.json 未能加载")
        End If

        _CurrentLanguage = Language
        ApplyCulture(Language)
        ApplyResourceDictionary()
    End Sub

    ''' <summary>
    ''' 获取语言文本。缺键时返回键本身（也就是基础语言的原文），并记录一次日志。
    ''' </summary>
    Public Function T(Key As String, ParamArray Args As Object()) As String
        If String.IsNullOrEmpty(Key) Then Return ""
        Key = NormalizeKey(Key)
        Dim Value As String = Nothing
        If Not LanguageData.TryGetValue(Key, Value) Then
            If MissingKeys.Add(Key) Then Logger.Warn($"缺少语言键：{Key}")
            Return Key
        End If
        If Args Is Nothing OrElse Args.Length = 0 Then Return Value
        Try
            Return String.Format(Value, Args)
        Catch ex As FormatException
            Logger.Warn(ex, $"语言键格式错误：{Key}")
            Return Value
        End Try
    End Function

    ''' <summary>
    ''' 检查语言键是否存在。
    ''' </summary>
    Public Function HasKey(Key As String) As Boolean
        Return Not String.IsNullOrEmpty(Key) AndAlso LanguageData.ContainsKey(NormalizeKey(Key))
    End Function

    ''' <summary>
    ''' 统一键中的换行符，避免 vbCrLf 与 JSON 中的 \n 不匹配。
    ''' </summary>
    Private Function NormalizeKey(Key As String) As String
        If String.IsNullOrEmpty(Key) Then Return ""
        Return Key.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
    End Function

    ''' <summary>
    ''' 获取语言在界面上的显示名称。显示名来自语言清单，避免翻译后语言名互相看不懂。
    ''' </summary>
    Public Function GetDisplayName(Code As String) As String
        Code = NormalizeLanguage(Code)
        Dim Name As String = Nothing
        If LanguageDisplayNames.TryGetValue(Code, Name) AndAlso Not String.IsNullOrWhiteSpace(Name) Then Return Name
        Return Code
    End Function

    ''' <summary>
    ''' 规范化语言代码，例如将 zh-CN 转换为 zh_cn。
    ''' </summary>
    Public Function NormalizeLanguage(Language As String) As String
        If String.IsNullOrWhiteSpace(Language) Then Return DefaultLanguage
        Return Language.Trim().Replace("-", "_").ToLowerInvariant()
    End Function

    ''' <summary>
    ''' 将语言代码转换为 .NET 区域代码，例如 zh_cn -> zh-CN。
    ''' </summary>
    Public Function ToCultureName(Language As String) As String
        Language = NormalizeLanguage(Language)
        If Language.Contains("_") Then
            Dim Parts = Language.Split("_"c)
            Return Parts(0) & "-" & Parts(1).ToUpperInvariant()
        End If
        Return Language
    End Function

    Private Sub LoadEmbeddedLanguage(Language As String)
        Dim Text As String = ReadEmbeddedLanguage(Language)
        If String.IsNullOrWhiteSpace(Text) Then
            Logger.Warn($"内嵌语言文件不存在：{Language}")
            Return
        End If
        MergeLanguageJson(Text, Language)
    End Sub

    Private Sub LoadExternalLanguage(Language As String)
        Try
            Dim ExternalPath As String = Paths.Base & "FXL\Languages\" & Language & ".json"
            If Not FileUtils.Exists(ExternalPath) Then Return
            Dim ExternalText As String = FileUtils.TryReadAsString(ExternalPath, Encoding.UTF8)
            If Not String.IsNullOrWhiteSpace(ExternalText) Then MergeLanguageJson(ExternalText, Language)
        Catch ex As Exception
            Logger.Warn(ex, $"读取外部语言文件失败：{Language}")
        End Try
    End Sub

    ''' <summary>
    ''' 将 JSON 逐键合并到当前语言数据中。外部语言包只覆盖同名的键，不做整体替换。
    ''' </summary>
    Private Sub MergeLanguageJson(Text As String, Language As String)
        Try
            Dim Root As JObject = JObject.Parse(Text)
            For Each Item As JProperty In Root.Properties()
                If Item.Value Is Nothing Then Continue For
                LanguageData(NormalizeKey(Item.Name)) = Item.Value.ToString()
            Next
        Catch ex As Exception
            Logger.Error(ex, $"语言文件解析失败：{Language}")
        End Try
    End Sub

    Private Function ReadEmbeddedLanguage(Language As String) As String
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            Dim ResourceName As String = Assembly.GetManifestResourceNames().
                FirstOrDefault(Function(Name) Name.EndsWith(ResourceMarker & Language & ".json", StringComparison.OrdinalIgnoreCase) OrElse
                                                   Name.EndsWith("." & Language & ".json", StringComparison.OrdinalIgnoreCase))
            If ResourceName Is Nothing Then Return ""
            Using Stream = Assembly.GetManifestResourceStream(ResourceName)
                If Stream Is Nothing Then Return ""
                Using Reader As New IO.StreamReader(Stream, Encoding.UTF8, True)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Error(ex, $"读取内嵌语言文件失败：{Language}")
            Return ""
        End Try
    End Function

    Private Sub RefreshAvailableLanguages()
        AvailableLanguageCache.Clear()
        LanguageDisplayNames.Clear()

        '读取语言清单（外部清单优先，便于用户添加语言）
        LoadManifest(ReadEmbeddedManifest())
        LoadManifest(ReadExternalManifest())

        '扫描内嵌语言包
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            For Each ResourceName In Assembly.GetManifestResourceNames()
                If Not ResourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim NoExtension As String = ResourceName.Substring(0, ResourceName.Length - 5)
                Dim Index As Integer = NoExtension.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase)
                If Index < 0 Then Continue For
                AddAvailableLanguage(NoExtension.Substring(Index + ResourceMarker.Length))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描内嵌语言包失败")
        End Try

        '扫描用户语言包
        Try
            Dim Folder As String = Paths.Base & "FXL\Languages\"
            If DirectoryUtils.Exists(Folder) Then
                For Each File In DirectoryUtils.EnumerateFiles(Folder, False, "*.json")
                    Dim Language As String = Path.GetFileNameWithoutExtension(File).ToLowerInvariant()
                    If Language <> "manifest" Then AddAvailableLanguage(Language)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "扫描外部语言包失败")
        End Try

        '基础语言必须始终可用，且排在第一位
        AvailableLanguageCache.RemoveAll(Function(Code) Code = DefaultLanguage)
        AvailableLanguageCache.Insert(0, DefaultLanguage)
        If Not LanguageDisplayNames.ContainsKey(DefaultLanguage) Then
            LanguageDisplayNames(DefaultLanguage) = DefaultLanguage
        End If
    End Sub

    Private Sub AddAvailableLanguage(Language As String)
        Language = NormalizeLanguage(Language)
        If String.IsNullOrWhiteSpace(Language) OrElse Language = "manifest" Then Return
        If Not AvailableLanguageCache.Contains(Language) Then AvailableLanguageCache.Add(Language)
    End Sub

    Private Function ReadEmbeddedManifest() As String
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            Dim ResourceName As String = Assembly.GetManifestResourceNames().
                FirstOrDefault(Function(Name) Name.EndsWith(ResourceMarker & ManifestFileName, StringComparison.OrdinalIgnoreCase))
            If ResourceName Is Nothing Then Return ""
            Using Stream = Assembly.GetManifestResourceStream(ResourceName)
                If Stream Is Nothing Then Return ""
                Using Reader As New IO.StreamReader(Stream, Encoding.UTF8, True)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取内嵌语言清单失败")
            Return ""
        End Try
    End Function

    Private Function ReadExternalManifest() As String
        Try
            Dim ExternalPath As String = Paths.Base & "FXL\Languages\" & ManifestFileName
            If FileUtils.Exists(ExternalPath) Then Return FileUtils.TryReadAsString(ExternalPath, Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "读取外部语言清单失败")
        End Try
        Return ""
    End Function

    Private Sub LoadManifest(Text As String)
        If String.IsNullOrWhiteSpace(Text) Then Return
        Try
            Dim Root As JObject = JObject.Parse(Text)
            Dim Languages = TryCast(Root("languages"), JArray)
            If Languages Is Nothing Then Return
            For Each Item As JObject In Languages.OfType(Of JObject)()
                Dim Code As String = NormalizeLanguage(Item("code")?.ToString())
                If String.IsNullOrWhiteSpace(Code) Then Continue For
                AddAvailableLanguage(Code)
                Dim Name As String = Item("name")?.ToString()
                If Not String.IsNullOrWhiteSpace(Name) Then LanguageDisplayNames(Code) = Name
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析语言清单失败")
        End Try
    End Sub

    Private Sub ApplyCulture(Language As String)
        Try
            Dim Culture As New CultureInfo(ToCultureName(Language))
            '只修改 UI 语言，不修改 CurrentCulture，避免影响日期、数字解析和排序
            CultureInfo.DefaultThreadCurrentUICulture = Culture
            CultureInfo.CurrentUICulture = Culture
            'Language 的元数据只能覆盖一次，且必须在创建任何控件之前完成
            If Not _IsLanguageMetadataApplied Then
                _IsLanguageMetadataApplied = True
                Dim XmlLang = XmlLanguage.GetLanguage(Culture.IetfLanguageTag)
                FrameworkElement.LanguageProperty.OverrideMetadata(GetType(FrameworkElement), New FrameworkPropertyMetadata(XmlLang))
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"应用界面语言失败：{Language}")
        End Try
    End Sub

    Private Sub ApplyResourceDictionary()
        Try
            Dim App = Application.Current
            If App Is Nothing Then Return
            Dim NewDictionary As New ResourceDictionary()
            For Each Item In LanguageData
                NewDictionary(Item.Key) = Item.Value
            Next
            If LanguageDictionary IsNot Nothing Then App.Resources.MergedDictionaries.Remove(LanguageDictionary)
            LanguageDictionary = NewDictionary
            App.Resources.MergedDictionaries.Add(LanguageDictionary)
        Catch ex As Exception
            Logger.Error(ex, "应用语言资源字典失败")
        End Try
    End Sub

End Module
