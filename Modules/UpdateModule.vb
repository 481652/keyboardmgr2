'处理程序更新
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports System.Web.Script.Serialization

Public Class UpdateRelease
    Public Property tag_name As String
    Public Property html_url As String
    Public Property assets As UpdateAsset()
End Class

Public Class UpdateAsset
    Public Property name As String
    Public Property browser_download_url As String
    Public Property digest As String
End Class

Public Module UpdateModule
    Public Const LatestReleaseApiUrl As String = "https://api.github.com/repos/481652/keyboardmgr2/releases/latest"
    Public Const ReleasesPageUrl As String = "https://github.com/481652/keyboardmgr2/releases/latest"
    Public Const MaximumDownloadBytes As Long = 512L * 1024L * 1024L
    Private Const MaximumManifestBytes As Integer = 1024 * 1024
    Private Const MaximumExtractedBytes As Long = 1024L * 1024L * 1024L
    Private Const MaximumArchiveEntries As Integer = 5000

    Public Function CreateHttpClient() As HttpClient
        Dim client As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(10)}
        client.DefaultRequestHeaders.Add("User-Agent", "keyboardmgr2-update-checker")
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
        Return client
    End Function

    Public Async Function GetLatestReleaseAsync(httpClient As HttpClient) As Task(Of UpdateRelease)
        Using response As HttpResponseMessage = Await httpClient.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead)
            response.EnsureSuccessStatusCode()
            EnsureHttps(response.RequestMessage.RequestUri, "更新信息地址")
            If response.Content.Headers.ContentLength.HasValue AndAlso response.Content.Headers.ContentLength.Value > MaximumManifestBytes Then
                Throw New InvalidDataException("更新信息过大。")
            End If
            Dim bytes As Byte() = Await response.Content.ReadAsByteArrayAsync()
            If bytes.Length > MaximumManifestBytes Then Throw New InvalidDataException("更新信息过大。")
            Dim serializer As New JavaScriptSerializer With {.MaxJsonLength = MaximumManifestBytes}
            Dim release As UpdateRelease = serializer.Deserialize(Of UpdateRelease)(Encoding.UTF8.GetString(bytes))
            If release Is Nothing OrElse String.IsNullOrWhiteSpace(release.tag_name) Then Throw New FormatException("更新信息格式错误。")
            Return release
        End Using
    End Function

    Public Function GetReleaseVersion(release As UpdateRelease) As Version
        Dim versionText As String = release.tag_name.Trim().TrimStart("v"c, "V"c)
        Dim version As Version = Nothing
        If Not Version.TryParse(versionText, version) Then Throw New FormatException("发布版本号格式错误。")
        Return version
    End Function

    Public Function GetZipAsset(release As UpdateRelease) As UpdateAsset
        If release.assets Is Nothing Then Throw New FormatException("该版本没有可下载的更新包。")
        Dim asset As UpdateAsset = release.assets.FirstOrDefault(Function(value) value IsNot Nothing AndAlso value.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        If asset Is Nothing OrElse String.IsNullOrWhiteSpace(asset.browser_download_url) Then Throw New FormatException("该版本没有 ZIP 更新包。")
        EnsureHttps(New Uri(asset.browser_download_url, UriKind.Absolute), "更新下载地址")
        Return asset
    End Function

    Public Async Function DownloadUpdateAsync(httpClient As HttpClient, downloadUri As Uri, destinationPath As String) As Task
        Using response As HttpResponseMessage = Await httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead)
            response.EnsureSuccessStatusCode()
            EnsureHttps(response.RequestMessage.RequestUri, "更新下载地址")
            If response.Content.Headers.ContentLength.HasValue AndAlso response.Content.Headers.ContentLength.Value > MaximumDownloadBytes Then
                Throw New InvalidDataException("更新包超过 512 MB 限制。")
            End If
            Using source As Stream = Await response.Content.ReadAsStreamAsync(), destination As New FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, True)
                Dim buffer(81919) As Byte
                Dim total As Long = 0
                Do
                    Dim read As Integer = Await source.ReadAsync(buffer, 0, buffer.Length)
                    If read = 0 Then Exit Do
                    total += read
                    If total > MaximumDownloadBytes Then Throw New InvalidDataException("更新包超过 512 MB 限制。")
                    Await destination.WriteAsync(buffer, 0, read)
                Loop
            End Using
        End Using
    End Function

    Public Sub VerifyFileSha256(filePath As String, digest As String)
        If String.IsNullOrWhiteSpace(digest) Then Throw New CryptographicException("更新发布未提供 SHA-256 摘要。")
        Dim expectedHash As String = digest.Trim()
        If expectedHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then expectedHash = expectedHash.Substring(7)
        If expectedHash.Length <> 64 OrElse Not expectedHash.All(Function(character) Uri.IsHexDigit(character)) Then Throw New FormatException("更新包 SHA-256 格式错误。")
        Dim actualHash As String
        Using stream As Stream = File.OpenRead(filePath), sha256 As SHA256 = SHA256.Create()
            actualHash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty)
        End Using
        If Not String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then Throw New CryptographicException("更新包 SHA-256 校验失败。")
    End Sub

    Public Sub ExtractUpdateSafely(zipPath As String, destinationDirectory As String)
        Directory.CreateDirectory(destinationDirectory)
        Dim destinationRoot As String = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) & Path.DirectorySeparatorChar
        Using archive As ZipArchive = ZipFile.OpenRead(zipPath)
            If archive.Entries.Count > MaximumArchiveEntries Then Throw New InvalidDataException("更新包文件数量过多。")
            Dim totalExtracted As Long = 0
            For Each entry As ZipArchiveEntry In archive.Entries
                totalExtracted += entry.Length
                If totalExtracted > MaximumExtractedBytes Then Throw New InvalidDataException("更新包解压后超过 1 GB 限制。")
                Dim targetPath As String = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName))
                If Not targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("更新包包含非法路径。")
                If String.IsNullOrEmpty(entry.Name) Then
                    Directory.CreateDirectory(targetPath)
                Else
                    Dim parent As String = Path.GetDirectoryName(targetPath)
                    If Not String.IsNullOrEmpty(parent) Then Directory.CreateDirectory(parent)
                    entry.ExtractToFile(targetPath, False)
                End If
            Next
        End Using
    End Sub

    Public Function FindUpdateExecutable(extractPath As String) As String
        Dim executables As String() = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories)
        If executables.Length <> 1 Then Throw New InvalidDataException("更新包中必须包含且只能包含一个主程序 EXE。")
        Dim assemblyName As Reflection.AssemblyName
        Try
            assemblyName = Reflection.AssemblyName.GetAssemblyName(executables(0))
        Catch ex As Exception
            Throw New InvalidDataException("更新包中的主程序不是有效的 .NET 程序集。", ex)
        End Try
        If Not String.Equals(assemblyName.Name, "keyboardmgr2", StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("更新包中的主程序身份无效。")
        Return executables(0)
    End Function

    Private Sub EnsureHttps(uri As Uri, description As String)
        If uri Is Nothing OrElse uri.Scheme <> Uri.UriSchemeHttps Then Throw New FormatException(description & "必须使用 HTTPS。")
    End Sub
End Module
