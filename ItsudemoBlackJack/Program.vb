Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Web
Imports Npgsql

Module Program
    Dim PORT As Integer = 8097
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()
    Dim TABLE_NAME As String = "itsudemoblackjack"

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Try
            EnsureLeaderboardTable()
            Dim listener As New HttpListener()
            listener.Prefixes.Add($"http://*:{PORT}/")
            listener.Start()
            Console.WriteLine($"Server listening on http://127.0.0.1:{PORT}/")
            Console.WriteLine("Press Ctrl+C to stop.")

            While True
                Dim context = listener.GetContext()
                Threading.ThreadPool.QueueUserWorkItem(AddressOf HandleRequest, context)
            End While

        Catch ex As Exception
            Console.WriteLine("Fatal error: " & ex.ToString())
        End Try
    End Sub
    Private Sub HandleRequest(state As Object)
        Dim context = DirectCast(state, HttpListenerContext)
        Try
            Dim request = context.Request
            LogRequest(request)
            Dim path = request.Url.AbsolutePath

            Select Case path.ToLowerInvariant()
                Case "/rpg/ac00/fac01i.php"
                    HandleHiScore(context)
                Case "/rpg/ac00/fac01r.php"
                    HandleRanking(context)
                Case Else
                    SendTextResponse(context, "Not Found", 404)
            End Select

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Try
                SendTextResponse(context, "Internal Server Error", 500)
            Catch
                ' ignore
            End Try
        End Try
    End Sub

    Private Sub HandleHiScore(context As HttpListenerContext)
        Dim request = context.Request
        Dim q = HttpUtility.ParseQueryString(request.Url.Query)

        Dim uid As String = q("uid")
        If String.IsNullOrWhiteSpace(uid) Then uid = "NULLGWDOCOMO"

        Dim scoreStr As String = q("s")
        Dim score As Integer = 0
        Integer.TryParse(scoreStr, score)

        Dim isReadOnly As Boolean = (q("r") = "1")

        ' Update score unless read-only
        If Not isReadOnly Then
            UpsertScore(uid, score)
        End If

        ' Fetch top 4
        Dim topScores = GetTopScores(4)

        Dim parts As New List(Of String)

        ' Add real scores first
        For Each row In topScores
            Dim name = If(String.IsNullOrWhiteSpace(row.Name), "CPU", row.Name)
            parts.Add($"{name}/{row.Score}")
        Next

        Dim padIndex As Integer = 1
        While parts.Count < 4
            parts.Add($"CPU{padIndex}/0")
            padIndex += 1
        End While

        Dim responseBody As String = String.Join(",", parts.Take(4))

        SendTextResponse(context, responseBody, 200)

    End Sub
    Private Sub UpsertScore(uid As String, newScore As Integer)
        Dim playername As String = uid ' default for now

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"
            INSERT INTO {TABLE_NAME} (uid, playername, score)
            VALUES (@uid, @playername, @score)
            ON CONFLICT (uid) DO UPDATE
            SET
                playername = COALESCE(NULLIF(EXCLUDED.playername, ''), {TABLE_NAME}.playername),
                score = GREATEST({TABLE_NAME}.score, EXCLUDED.score),
                updated_at = NOW();
        "

            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@uid", uid)
                cmd.Parameters.AddWithValue("@playername", playername)
                cmd.Parameters.AddWithValue("@score", newScore)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub
    Private Function GetTopScores(limit As Integer) As List(Of (Name As String, Score As Integer))
        Dim results As New List(Of (Name As String, Score As Integer))

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"SELECT uid, score FROM {TABLE_NAME} ORDER BY score DESC, uid ASC LIMIT @lim;"
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@lim", limit)

                Using r = cmd.ExecuteReader()
                    While r.Read()
                        Dim name = r.GetString(0)
                        Dim score = r.GetInt32(1)
                        results.Add((name, score))
                    End While
                End Using
            End Using
        End Using

        Return results
    End Function
    Private Sub HandleRanking(context As HttpListenerContext)
        Dim request = context.Request
        Dim q = HttpUtility.ParseQueryString(request.Url.Query)

        Dim rStr As String = q("r")
        Dim board As Integer = 4
        Integer.TryParse(rStr, board)
        ' TODO: filter by board (today/week/month/all-time)
        Dim topScores = GetTopScores(30) ' or whatever rankmax is

        Dim parts As New List(Of String)

        For Each row In topScores
            Dim name = If(String.IsNullOrWhiteSpace(row.Name), "CPU", row.Name)
            name = name.Replace(",", "_").Replace("/", "_")
            parts.Add($"{name}/{row.Score}")
        Next
        Dim pad As Integer = 1
        While parts.Count < 20
            parts.Add($"CPU{pad}/0")
            pad += 1
        End While

        Dim body As String = String.Join(",", parts)

        Console.WriteLine("Ranking Response: " & body)
        SendTextResponse(context, body, 200)
    End Sub
    Private Sub EnsureLeaderboardTable()
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"
            CREATE TABLE IF NOT EXISTS {TABLE_NAME} (
                uid        TEXT PRIMARY KEY,
                playername TEXT NOT NULL,
                score      INTEGER NOT NULL DEFAULT 0,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        "

            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
            Dim idxSql As String = $"
            CREATE INDEX IF NOT EXISTS idx_{TABLE_NAME}_score
            ON {TABLE_NAME} (score DESC);
        "

            Using idxCmd As New NpgsqlCommand(idxSql, conn)
                idxCmd.ExecuteNonQuery()
            End Using
            Console.WriteLine("DB Connected & Leaderboard table ensured.")
        End Using
    End Sub
    Private Sub SendTextResponse(context As HttpListenerContext, body As String, statusCode As Integer)
        Dim bytes = Encoding.UTF8.GetBytes(body)

        context.Response.StatusCode = statusCode
        context.Response.ContentType = "text/plain; charset=utf-8"
        context.Response.ContentEncoding = Encoding.UTF8
        context.Response.ContentLength64 = bytes.Length

        Using output = context.Response.OutputStream
            output.Write(bytes, 0, bytes.Length)
        End Using
    End Sub
    Private Sub LogRequest(request As HttpListenerRequest)
        Console.WriteLine("--------------------------------------------------")
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Incoming Request")
        Console.WriteLine($"{request.HttpMethod} {request.Url}")

        Console.WriteLine($"Remote: {request.RemoteEndPoint?.Address}:{request.RemoteEndPoint?.Port}")
        Console.WriteLine($"Local:  {request.LocalEndPoint?.Address}:{request.LocalEndPoint?.Port}")

        Console.WriteLine($"Path:   {request.Url.AbsolutePath}")
        Console.WriteLine($"Query:  {request.Url.Query}")

        ' Parsed query parameters
        If request.Url.Query.Length > 1 Then
            Dim qs = HttpUtility.ParseQueryString(request.Url.Query)
            Console.WriteLine("Query Parameters:")
            For Each key As String In qs.AllKeys
                Console.WriteLine($"  {key} = {qs(key)}")
            Next
        End If

        ' Headers
        Console.WriteLine("Headers:")
        For Each key As String In request.Headers.AllKeys
            Console.WriteLine($"  {key}: {request.Headers(key)}")
        Next

        ' Body (POST / PUT)
        If request.HasEntityBody Then
            Using reader As New StreamReader(request.InputStream, request.ContentEncoding)
                Dim body As String = reader.ReadToEnd()
                If Not String.IsNullOrWhiteSpace(body) Then
                    Console.WriteLine("Body:")
                    Console.WriteLine(body)
                End If
            End Using
        End If

        Console.WriteLine("--------------------------------------------------")
    End Sub
End Module
