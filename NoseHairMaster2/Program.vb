Imports System.Net
Imports System.Text
Imports System.Web
Imports System.IO
Imports Npgsql

Module Program
    Dim PORT As Integer = 8095
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()
    Dim TABLE_NAME As String = "nosehairmaster2"
    Dim FULLWIDTH_ASTERISK As String = ChrW(&HFF0A)

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
            Dim path = request.Url.AbsolutePath
            Dim method = request.HttpMethod.ToUpperInvariant()

            If path.Equals("/bakaservlet", StringComparison.OrdinalIgnoreCase) AndAlso
               method = "POST" Then

                HandleLeaderboardRequest(context)
            Else
                SendTextResponse(context, "Not Found", 404)
            End If

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Try
                SendTextResponse(context, "Internal Server Error", 500)
            Catch
                ' ignore
            End Try
        End Try
    End Sub
    Private Sub HandleLeaderboardRequest(context As HttpListenerContext)
        Dim request = context.Request

        ' uid from query string (?uid=XXXX)
        Dim query = HttpUtility.ParseQueryString(request.Url.Query)
        Dim uid As String = query("uid")
        If String.IsNullOrEmpty(uid) Then
            Console.WriteLine("Missing uid, defaulting to NULLGWDOCOMO.")
            uid = "NULLGWDOCOMO"
        End If
        If uid.ToUpper = "NULLGWDOCOMO" Then
            Return
        End If

        ' Read raw body
        Dim body As Byte()
        Using ms As New MemoryStream()
            request.InputStream.CopyTo(ms)
            body = ms.ToArray()
        End Using

        If body.Length < 12 Then
            ' Error: not enough data
            SendInt32BEResponse(context, 0)
            Return
        End If

        Try
            Dim unk1 As Integer = ReadInt32BE(body, 0)
            Dim mode As Integer = ReadInt32BE(body, 4)
            Dim timeCentisec As Integer = ReadInt32BE(body, 8)

            Console.WriteLine($"uid={uid}, unk1={unk1}, mode={mode}, time={timeCentisec}")

            Select Case mode
                Case 2
                    HandleRankCheck(context, uid)
                Case 3
                    HandleScoreSubmission(context, uid, timeCentisec)
                Case Else
                    ' Unsupported mode
                    SendInt32BEResponse(context, 0)
            End Select

        Catch ex As Exception
            Console.WriteLine("Error parsing body: " & ex.Message)
            SendInt32BEResponse(context, 0)
        End Try
    End Sub
    Private Sub HandleRankCheck(context As HttpListenerContext, uid As String)
        Dim sb As New StringBuilder()
        sb.Append("MISSION COMPLETE!").Append(FULLWIDTH_ASTERISK).AppendLine()

        Dim bestTimeOpt = GetUserBestTime(uid)

        If Not bestTimeOpt.HasValue Then
            sb.AppendLine("Rank not found")
        Else
            Dim bestTime = bestTimeOpt.Value
            sb.Append("Best time: ").Append(FormatTime(bestTime)).AppendLine()
            Dim rank = GetRank(bestTime)
            sb.Append("Current rank: ").Append(rank)
        End If

        Dim responseBytes = BuildJavaUTFResponse(sb.ToString())
        SendBinaryResponse(context, responseBytes, 200)
    End Sub
    Private Sub HandleScoreSubmission(context As HttpListenerContext, uid As String, currentTime As Integer)
        Dim sb As New StringBuilder()
        sb.Append("RANKING").Append(FULLWIDTH_ASTERISK).AppendLine()

        Dim bestTimeOpt = GetUserBestTime(uid)

        If Not bestTimeOpt.HasValue OrElse currentTime < bestTimeOpt.Value Then
            ' New personal record
            UpdateUserBestTime(uid, currentTime)
            Dim rank = GetRank(currentTime)
            sb.AppendLine("New personal record!")
            sb.Append("Current rank: ").Append(rank)
        Else
            ' Not a new record
            Dim bestTime = bestTimeOpt.Value
            Dim rank = GetRank(bestTime)
            sb.Append("Best time: ").Append(FormatTime(bestTime)).AppendLine()
            sb.Append("Current rank: ").Append(rank)
        End If

        Dim responseBytes = BuildJavaUTFResponse(sb.ToString())
        SendBinaryResponse(context, responseBytes, 200)
    End Sub

    ' Format centiseconds as "<seconds>.<cc>"
    Private Function FormatTime(centiseconds As Integer) As String
        Dim wholeSeconds As Integer = centiseconds \ 100
        Dim cs As Integer = centiseconds Mod 100
        Return $"{wholeSeconds}.{cs:00}"
    End Function
    Private Function BuildJavaUTFResponse(text As String) As Byte()
        Dim utf8Bytes = Encoding.UTF8.GetBytes(text)
        Dim length As Integer = utf8Bytes.Length

        Using ms As New MemoryStream()
            Using bw As New BinaryWriter(ms)
                ' 16-bit big-endian length
                Dim lenBytes = BitConverter.GetBytes(CUShort(length))
                If BitConverter.IsLittleEndian Then
                    Array.Reverse(lenBytes)
                End If
                bw.Write(lenBytes)

                ' UTF-8 payload
                bw.Write(utf8Bytes)

                ' Two 32-bit big-endian zeros
                bw.Write(GetUInt32BEBytes(0UI))
                bw.Write(GetUInt32BEBytes(0UI))
            End Using
            Return ms.ToArray()
        End Using
    End Function

    ' Ensure table exists: uid TEXT PK, time_centisec INTEGER NOT NULL
    Private Sub EnsureLeaderboardTable()
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Dim sql As String = $"
                CREATE TABLE IF NOT EXISTS {TABLE_NAME} (
                    uid           TEXT PRIMARY KEY,
                    time_centisec INTEGER NOT NULL
                );
            "
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    ' Get user's best time
    Private Function GetUserBestTime(uid As String) As Integer?
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Dim sql As String = $"SELECT time_centisec FROM {TABLE_NAME} WHERE uid = @uid LIMIT 1;"
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("uid", uid)
                Dim result = cmd.ExecuteScalar()
                If result Is Nothing OrElse result Is DBNull.Value Then
                    Return Nothing
                Else
                    Return Convert.ToInt32(result)
                End If
            End Using
        End Using
    End Function

    ' Insert or update best time (lower = better)
    Private Sub UpdateUserBestTime(uid As String, newTime As Integer)
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Dim sql As String = $"
                INSERT INTO {TABLE_NAME} (uid, time_centisec)
                VALUES (@uid, @time)
                ON CONFLICT (uid) DO UPDATE
                SET time_centisec = LEAST({TABLE_NAME}.time_centisec, EXCLUDED.time_centisec);
            "
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("uid", uid)
                cmd.Parameters.AddWithValue("time", newTime)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub
    Private Function GetRank(timeCentisec As Integer) As Integer
        Dim times As New List(Of Integer)()

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Dim sql As String = $"SELECT time_centisec FROM {TABLE_NAME} ORDER BY time_centisec ASC;"
            Using cmd As New NpgsqlCommand(sql, conn)
                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        times.Add(reader.GetInt32(0))
                    End While
                End Using
            End Using
        End Using

        Dim index As Integer = times.IndexOf(timeCentisec)
        If index >= 0 Then
            Return index + 1
        Else
            Return times.Count + 1
        End If
    End Function
    Private Function ReadInt32BE(data As Byte(), offset As Integer) As Integer
        Dim b(3) As Byte
        Array.Copy(data, offset, b, 0, 4)
        If BitConverter.IsLittleEndian Then
            Array.Reverse(b)
        End If
        Return BitConverter.ToInt32(b, 0)
    End Function
    Private Function GetInt32BEBytes(value As Integer) As Byte()
        Dim b = BitConverter.GetBytes(value)
        If BitConverter.IsLittleEndian Then
            Array.Reverse(b)
        End If
        Return b
    End Function
    Private Function GetUInt32BEBytes(value As UInteger) As Byte()
        Dim b = BitConverter.GetBytes(value)
        If BitConverter.IsLittleEndian Then
            Array.Reverse(b)
        End If
        Return b
    End Function
    Private Sub SendInt32BEResponse(context As HttpListenerContext, value As Integer)
        Dim data = GetInt32BEBytes(value)
        SendBinaryResponse(context, data, 200)
    End Sub
    Private Sub SendBinaryResponse(context As HttpListenerContext, data As Byte(), statusCode As Integer)
        context.Response.StatusCode = statusCode
        context.Response.ContentType = "application/octet-stream"
        context.Response.ContentLength64 = data.Length
        Using output = context.Response.OutputStream
            output.Write(data, 0, data.Length)
        End Using
    End Sub
    Private Sub SendTextResponse(context As HttpListenerContext, body As String, statusCode As Integer)
        Dim bytes = Encoding.UTF8.GetBytes(body)
        context.Response.StatusCode = statusCode
        context.Response.ContentType = "text/plain; charset=utf-8"
        context.Response.ContentLength64 = bytes.Length
        Using output = context.Response.OutputStream
            output.Write(bytes, 0, bytes.Length)
        End Using
    End Sub

End Module
