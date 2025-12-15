Imports System
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Web
Imports System.Collections.Specialized

Module RaceServer

    Private Const PORT As Integer = 8101
    Private ReadOnly BaseDir As String = AppContext.BaseDirectory
    Private ReadOnly SvrDataDir As String = Path.Combine(BaseDir, "svrdata")
    Private ReadOnly Rng As New Random()

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Dim listener As New HttpListener()
        listener.Prefixes.Add($"http://*:{PORT}/")
        listener.Start()

        Console.WriteLine($"Race server listening on http://127.0.0.1:{PORT}/")
        Console.WriteLine("Press Ctrl+C to stop.")

        While True
            Dim ctx = listener.GetContext()
            ThreadPool.QueueUserWorkItem(AddressOf HandleRequest, ctx)
        End While
    End Sub
    Private Sub HandleRequest(state As Object)
        Dim context = DirectCast(state, HttpListenerContext)
        Try
            Dim req = context.Request
            Dim path = req.Url.AbsolutePath
            Dim qs = HttpUtility.ParseQueryString(req.Url.Query)

            If req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) Then
                HandleGet(context, path, qs)
            ElseIf req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) Then
                HandlePost(context, path, qs)
            Else
                SendBytes(context, Encoding.ASCII.GetBytes("0"), "application/octet-stream")
            End If

        Catch ex As Exception
            Console.WriteLine("HandleRequest error: " & ex.Message)
            Try
                SendBytes(context, Encoding.ASCII.GetBytes("0"), "application/octet-stream")
            Catch
            End Try
        End Try
    End Sub

    ' GET routing
    Private Sub HandleGet(context As HttpListenerContext, path As String, qs As NameValueCollection)
        Dim buffer As Byte()

        Select Case path
            Case "/appi/race/mission.php"
                buffer = HandleMission(qs)

            Case "/appi/race/tournament.php"
                buffer = HandleTournament(qs)

            Case "/appi/race/advice.php"
                buffer = HandleAdvice()

            Case "/appi/race/ghost.php"
                buffer = HandleGhost()

            Case "/appi/race/dlfile.php"
                buffer = HandleDlFile(qs)

            Case "/appi/race/chara.php"
                buffer = HandleChara(qs)

            Case "/appi/race/initiate.php"
                buffer = Encoding.ASCII.GetBytes("1")

            Case "/appi/race/isregist.php"
                buffer = HandleIsRegist(qs)

            Case Else
                Console.WriteLine($"Unknown GET: {path} ? {qs}")
                buffer = Encoding.ASCII.GetBytes("0")
        End Select

        SendBytes(context, buffer, "application/octet-stream")
    End Sub

    ' POST routing
    Private Sub HandlePost(context As HttpListenerContext, path As String, qs As NameValueCollection)
        Dim req = context.Request
        Dim bodyBytes As Byte()

        Using ms As New MemoryStream()
            req.InputStream.CopyTo(ms)
            bodyBytes = ms.ToArray()
        End Using

        Dim buffer As Byte()

        Select Case path
            Case "/appi/race/save.php"
                buffer = HandleSave(bodyBytes)

            Case "/appi/race/revGhost.php"
                buffer = HandleRevGhost(bodyBytes)

            Case Else
                Console.WriteLine($"Unknown POST: {path}")
                Console.WriteLine($"BodyLen={bodyBytes.Length}")
                buffer = Encoding.ASCII.GetBytes("0")
        End Select

        SendBytes(context, buffer, "application/octet-stream")
    End Sub

    ' GET handlers
    Private Function HandleMission(qs As NameValueCollection) As Byte()
        Dim mnid As Integer = SafeInt(qs("mnid"))
        Dim csvPath = Path.Combine(SvrDataDir, "missions.csv")

        Using ms As New MemoryStream()
            ms.Write(Encoding.ASCII.GetBytes("1" & vbCrLf))
            ms.Write(Encoding.ASCII.GetBytes((mnid + 1).ToString() & vbCrLf))

            If File.Exists(csvPath) Then
                Dim bytes = File.ReadAllBytes(csvPath)
                ms.Write(bytes, 0, bytes.Length)
            End If

            Return ms.ToArray()
        End Using
    End Function
    Private Function HandleTournament(qs As NameValueCollection) As Byte()
        Dim tkid As Integer = SafeInt(qs("tkid"))
        Dim csvPath = Path.Combine(SvrDataDir, "tournaments.csv")

        Using ms As New MemoryStream()
            ms.Write(Encoding.ASCII.GetBytes("1" & vbCrLf))
            ms.Write(Encoding.ASCII.GetBytes((tkid + 1).ToString() & vbCrLf))

            If File.Exists(csvPath) Then
                Dim bytes = File.ReadAllBytes(csvPath)
                ms.Write(bytes, 0, bytes.Length)
            End If

            Return ms.ToArray()
        End Using
    End Function
    Private Function HandleAdvice() As Byte()
        Dim csvPath = Path.Combine(SvrDataDir, "advices.csv")
        Using ms As New MemoryStream()
            ms.Write(Encoding.ASCII.GetBytes("1" & vbCrLf))

            If File.Exists(csvPath) Then
                Dim all = File.ReadAllBytes(csvPath)
                Dim lines = SplitBytes(all, AscW(ControlChars.Lf))
                If lines.Count > 0 Then
                    Dim pick = lines(Rng.Next(lines.Count))
                    ms.Write(pick, 0, pick.Length)
                End If
            End If

            Return ms.ToArray()
        End Using
    End Function
    Private Function HandleGhost() As Byte()
        Dim ghostPath = Path.Combine(SvrDataDir, "ghost.bin")

        If Not File.Exists(ghostPath) Then
            Return Encoding.ASCII.GetBytes("6")
        End If

        Using ms As New MemoryStream()
            ms.Write(Encoding.ASCII.GetBytes("1" & vbCrLf & "1" & vbCrLf))
            Dim bytes = File.ReadAllBytes(ghostPath)
            ms.Write(bytes, 0, bytes.Length)
            Return ms.ToArray()
        End Using
    End Function
    Private Function HandleDlFile(qs As NameValueCollection) As Byte()
        Dim name As String = qs("name")
        If String.IsNullOrWhiteSpace(name) Then
            Return Encoding.ASCII.GetBytes("3")
        End If

        Dim parts = name.Split("/"c)
        Dim relParts = If(parts.Length > 3, parts.Skip(3).ToArray(), Array.Empty(Of String)())

        Dim safePath = SvrDataDir
        For Each p In relParts
            If String.IsNullOrWhiteSpace(p) Then Continue For
            safePath = Path.Combine(safePath, p)
        Next

        Console.WriteLine("DL: " & safePath)

        If Not File.Exists(safePath) Then
            Return Encoding.ASCII.GetBytes("3")
        End If

        Return File.ReadAllBytes(safePath)
    End Function
    Private Function HandleChara(qs As NameValueCollection) As Byte()
        Dim chid As Integer = SafeInt(qs("chid"))
        Dim chLv As Integer = 1
        Dim chExp As Integer = 0

        Dim s = "1" & vbCrLf & chLv.ToString() & vbCrLf & chExp.ToString()
        Return Encoding.ASCII.GetBytes(s)
    End Function
    Private Function HandleIsRegist(qs As NameValueCollection) As Byte()
        Dim ver As String = qs("ver")
        If ver Is Nothing Then ver = ""

        Dim unixSeconds As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

        Dim s = "1" & vbCrLf & ver & vbCrLf & unixSeconds.ToString()
        Return Encoding.ASCII.GetBytes(s)
    End Function

    ' POST handlers
    Private Function HandleSave(body As Byte()) As Byte()
        Dim text = Encoding.ASCII.GetString(body)
        Dim form = HttpUtility.ParseQueryString(text)
        Console.WriteLine("SAVE:")
        For Each k As String In form.AllKeys
            Console.WriteLine($"  {k} = {form(k)}")
        Next

        ' Trial version note: no sync-back
        Return Encoding.ASCII.GetBytes("1")
    End Function
    Private Function HandleRevGhost(body As Byte()) As Byte()
        Dim marker = Encoding.ASCII.GetBytes("&chlog=")
        Dim idx = IndexOfBytes(body, marker)

        Dim left As Byte() = body
        Dim ghostData As Byte() = Array.Empty(Of Byte)()

        If idx >= 0 Then
            left = body.Take(idx).ToArray()
            ghostData = body.Skip(idx + marker.Length).ToArray()
        End If

        Dim leftText = Encoding.ASCII.GetString(left)
        Dim form = HttpUtility.ParseQueryString(leftText)

        Console.WriteLine("revGhost:")
        For Each k As String In form.AllKeys
            Console.WriteLine($"  {k} = {form(k)}")
        Next

        ' Save ghost
        Dim ghostPath = Path.Combine(SvrDataDir, "ghost.bin")
        Directory.CreateDirectory(SvrDataDir)
        File.WriteAllBytes(ghostPath, ghostData)

        Dim rank As Integer = 1
        Dim resp = "1" & vbCrLf & rank.ToString()
        Return Encoding.ASCII.GetBytes(resp)
    End Function

    ' Response helper
    Private Sub SendBytes(context As HttpListenerContext, data As Byte(), contentType As String)
        Dim resp = context.Response
        resp.StatusCode = 200
        resp.ContentType = contentType
        resp.ContentLength64 = data.Length
        Using os = resp.OutputStream
            os.Write(data, 0, data.Length)
        End Using
    End Sub

    ' Utility helpers
    Private Function SafeInt(s As String) As Integer
        Dim v As Integer = 0
        If Not String.IsNullOrWhiteSpace(s) Then Integer.TryParse(s, v)
        Return v
    End Function
    Private Function IndexOfBytes(haystack As Byte(), needle As Byte()) As Integer
        If needle Is Nothing OrElse needle.Length = 0 Then Return -1
        If haystack Is Nothing OrElse haystack.Length < needle.Length Then Return -1

        For i = 0 To haystack.Length - needle.Length
            Dim ok = True
            For j = 0 To needle.Length - 1
                If haystack(i + j) <> needle(j) Then
                    ok = False
                    Exit For
                End If
            Next
            If ok Then Return i
        Next

        Return -1
    End Function
    Private Function SplitBytes(data As Byte(), delimiter As Byte) As List(Of Byte())
        Dim parts As New List(Of Byte())()
        Dim start As Integer = 0
        For i = 0 To data.Length - 1
            If data(i) = delimiter Then
                Dim len = i - start
                If len > 0 Then
                    Dim chunk(len - 1) As Byte
                    Array.Copy(data, start, chunk, 0, len)
                    parts.Add(chunk)
                End If
                start = i + 1
            End If
        Next
        If start < data.Length Then
            Dim len = data.Length - start
            Dim chunk(len - 1) As Byte
            Array.Copy(data, start, chunk, 0, len)
            parts.Add(chunk)
        End If
        Return parts
    End Function

End Module