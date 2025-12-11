Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Web
Imports Npgsql

Module Program
    Dim PORT As Integer = 8095
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()
    Dim TABLE_NAME As String = "NoseHairMaster2"

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Try
            EnsureLeaderboardTable()

            Dim listener As New HttpListener()
            listener.Prefixes.Add($"http://*: {PORT}/".Replace(" ", ""))
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
            Dim path = request.Url.AbsolutePath

            Select Case path.ToLowerInvariant()
                Case "/b/p2_i.fcgi"
                    HandleMembership(context)
                Case "/appli/ranksrv"
                    HandleLeaderboard(context)
                Case Else
                    SendTextResponse(context, "Not Found", 404)
            End Select

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Try
                SendTextResponse(DirectCast(state, HttpListenerContext), "Internal Server Error", 500)
            Catch
                ' ignore
            End Try
        End Try
    End Sub
    Private Sub HandleMembership(context As HttpListenerContext)
        Dim request = context.Request
        Dim query = HttpUtility.ParseQueryString(request.Url.Query)
        Dim uid As String = query("uid")
        If String.IsNullOrEmpty(uid) Then
            uid = "NULLGWDOCOMO"
        End If

        If uid.ToUpper = "NULLGWDOCOMO" Then
            SendTextResponse(context, "0&0&Invalid UID", 400)
        End If

        ' You can later change logic here to actually check membership by UID.
        ' For now, mimic Python: "1&1&Welcome!"
        Dim responseData As String = "1&1&Welcome!"

        SendTextResponse(context, responseData, 200)
    End Sub
    Private Sub HandleLeaderboard(context As HttpListenerContext)
        Dim request = context.Request
        Dim query = HttpUtility.ParseQueryString(request.Url.Query)

        Dim uid As String = query("uid")
        If String.IsNullOrEmpty(uid) Then
            uid = "NULLGWDOCOMO"
        End If
        If uid.ToUpper = "NULLGWDOCOMO" Then
            SendTextResponse(context, "0&0&Invalid UID", 400)
        End If

        Dim currentScore As Integer = 0
        Dim sValue As String = query("s")
        If Not String.IsNullOrEmpty(sValue) Then
            Integer.TryParse(sValue, currentScore)
        End If

        ' Update / insert score in DB
        UpdateUserScore(uid, currentScore)

        ' Get top 14 scores
        Dim topScores = GetTop14Scores()

        ' Build response like: "1: 12345#2: 9999#...#14: 0"
        Dim parts As New List(Of String)()
        For i = 0 To topScores.Count - 1
            parts.Add($"{i + 1}: {topScores(i)}")
        Next

        Dim responseData As String = String.Join("#", parts)
        SendTextResponse(context, responseData, 200)
    End Sub

    Private Sub EnsureLeaderboardTable()
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Dim sql As String = $"
                CREATE TABLE IF NOT EXISTS {TABLE_NAME} (
                    uid   TEXT PRIMARY KEY,
                    score INTEGER NOT NULL DEFAULT 0
                );
            "
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Sub UpdateUserScore(uid As String, currentScore As Integer)
        ' If uid not present OR current_score > stored_score, update
        ' Use UPSERT with GREATEST to keep best score
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"
                INSERT INTO {TABLE_NAME} (uid, score)
                VALUES (@uid, @score)
                ON CONFLICT (uid) DO UPDATE
                SET score = GREATEST({TABLE_NAME}.score, EXCLUDED.score);
            "

            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("uid", uid)
                cmd.Parameters.AddWithValue("score", currentScore)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Function GetTop14Scores() As List(Of Integer)
        Dim scores As New List(Of Integer)()

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"
                SELECT score
                FROM {TABLE_NAME}
                ORDER BY score DESC
                LIMIT 14;
            "

            Using cmd As New NpgsqlCommand(sql, conn)
                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        scores.Add(reader.GetInt32(0))
                    End While
                End Using
            End Using
        End Using

        ' Pad with zeros up to 14 entries
        While scores.Count < 14
            scores.Add(0)
        End While

        Return scores
    End Function

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

End Module
