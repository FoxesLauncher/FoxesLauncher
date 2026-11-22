Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates

''' <summary>
''' 远程签名文档（更新 manifest、公告、动态配置）的验签工具。
''' 文档格式为：
'''   { "payload": "base64(原始 JSON)", "signature": { "alg": "RS256", "keyId": "...", "value": "base64(签名)" } }
''' 客户端先验证 payload 字节的签名，再解析 payload，避免 JSON 规范化带来的歧义。
''' </summary>
Friend Module ModRemoteCrypto

    ''' <summary>
    ''' 验签成功后的远程文档。
    ''' </summary>
    Friend Class RemoteSignedDocument
        ''' <summary>
        ''' 被签名的原始 JSON 字节。
        ''' </summary>
        Public PayloadBytes As Byte()
        ''' <summary>
        ''' 解析后的 payload。
        ''' </summary>
        Public Payload As JObject
        ''' <summary>
        ''' 使用的公钥标识。
        ''' </summary>
        Public KeyId As String
    End Class

    ''' <summary>
    ''' 验证远程签名文档。失败时返回 Nothing，并在 ErrorMessage 中给出原因。
    ''' </summary>
    Friend Function VerifySignedDocument(Raw As String, ByRef Document As RemoteSignedDocument, ByRef ErrorMessage As String) As Boolean
        Document = Nothing
        ErrorMessage = Nothing
        Try
            If String.IsNullOrWhiteSpace(Raw) Then
                ErrorMessage = T("返回内容为空")
                Return False
            End If
            If Raw.Length > 4 * 1024 * 1024 Then
                ErrorMessage = T("返回内容过大")
                Return False
            End If

            Dim Root As JObject
            Try
                Root = JObject.Parse(Raw)
            Catch ex As Exception
                ErrorMessage = T("不是有效的 JSON：{0}", ex.Message)
                Return False
            End Try

            Dim PayloadText = Root("payload")?.ToString
            Dim Signature = TryCast(Root("signature"), JObject)
            If String.IsNullOrWhiteSpace(PayloadText) OrElse Signature Is Nothing Then
                ErrorMessage = T("缺少 payload 或 signature")
                Return False
            End If

            Dim Alg = If(Signature("alg")?.ToString, "")
            If Not Alg.Equals("RS256", StringComparison.OrdinalIgnoreCase) Then
                ErrorMessage = T("不支持的签名算法：{0}", Alg)
                Return False
            End If

            Dim KeyId = If(Signature("keyId")?.ToString, "")
            Dim SignatureValue = If(Signature("value")?.ToString, "")
            If String.IsNullOrWhiteSpace(KeyId) OrElse String.IsNullOrWhiteSpace(SignatureValue) Then
                ErrorMessage = T("签名信息不完整")
                Return False
            End If

            Dim PublicKeyXml = FindPublicKey(KeyId)
            If String.IsNullOrWhiteSpace(PublicKeyXml) Then
                ErrorMessage = T("未知的签名公钥：{0}", KeyId)
                Return False
            End If

            Dim PayloadBytes As Byte()
            Dim SignatureBytes As Byte()
            Try
                PayloadBytes = Convert.FromBase64String(PayloadText)
                SignatureBytes = Convert.FromBase64String(SignatureValue)
            Catch ex As Exception
                ErrorMessage = T("签名或 payload 不是有效的 Base64：{0}", ex.Message)
                Return False
            End Try

            Dim Verified As Boolean
            Using Rsa As New RSACryptoServiceProvider()
                Rsa.PersistKeyInCsp = False
                Rsa.FromXmlString(PublicKeyXml)
                Verified = Rsa.VerifyData(PayloadBytes, SignatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            End Using
            If Not Verified Then
                ErrorMessage = T("签名校验失败")
                Return False
            End If

            Dim PayloadJson As JObject
            Try
                PayloadJson = JObject.Parse(Encoding.UTF8.GetString(PayloadBytes))
            Catch ex As Exception
                ErrorMessage = T("payload 不是有效的 JSON：{0}", ex.Message)
                Return False
            End Try

            Document = New RemoteSignedDocument With {
                .PayloadBytes = PayloadBytes,
                .Payload = PayloadJson,
                .KeyId = KeyId
            }
            Return True
        Catch ex As Exception
            ErrorMessage = T("校验远程文档时出错：{0}", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 从构建期配置中查找指定 keyId 的 RSA 公钥 XML。
    ''' 配置格式：keyId=base64(公钥 XML)，多个用分号分隔。
    ''' </summary>
    Private Function FindPublicKey(KeyId As String) As String
        If String.IsNullOrWhiteSpace(UpdatePublicKeys) Then Return Nothing
        For Each Entry In UpdatePublicKeys.Split(";"c)
            Dim Item = Entry.Trim()
            If Item.Length = 0 Then Continue For
            Dim Separator = Item.IndexOf("="c)
            If Separator <= 0 Then Continue For
            Dim Id = Item.Substring(0, Separator).Trim()
            If Not Id.Equals(KeyId, StringComparison.OrdinalIgnoreCase) Then Continue For
            Try
                Return Encoding.UTF8.GetString(Convert.FromBase64String(Item.Substring(Separator + 1).Trim()))
            Catch ex As Exception
                Logger.Warn(ex, $"解析更新公钥 {Id} 失败")
                Return Nothing
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' 校验文件的 Authenticode 签名。
    ''' 未配置期望指纹时跳过；配置后要求签名证书指纹一致。
    ''' </summary>
    Friend Function VerifyAuthenticode(FilePath As String, Optional ByRef ErrorMessage As String = Nothing) As Boolean
        ErrorMessage = Nothing
        Dim Expected = UpdateAuthenticodeThumbprint.Replace(" ", "").Replace("-", "").Trim()
        If Expected.Length = 0 Then Return True

        Try
            Dim RawCertificate = X509Certificate.CreateFromSignedFile(FilePath)
            Dim Certificate As New X509Certificate2(RawCertificate)
            Dim Actual = Certificate.Thumbprint.Replace(" ", "").Replace("-", "")
            If Not Actual.Equals(Expected, StringComparison.OrdinalIgnoreCase) Then
                ErrorMessage = T("代码签名证书指纹不匹配（期望 {0}，实际 {1}）", Expected, Actual)
                Return False
            End If
            Return True
        Catch ex As Exception
            ErrorMessage = T("读取代码签名失败：{0}", ex.Message)
            Return False
        End Try
    End Function

End Module
