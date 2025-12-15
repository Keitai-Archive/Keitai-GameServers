Imports System.Collections.Specialized

Public Class ApiRequest
    Public Property Guid As String
    Public Property UID As String
    Public Property ID As String
    Public Property Action As String
    Public Property Mode As String
    Public Property Act As String
    Public Property App As String
    Public Property Ver As String
    Public Property Time As String
    Public Property ApiVer As String
    Public Property OwnerId As String
    Public Property ModelId As String
    Public Property TableId As String
    Public Property ReqId As String
    Public Property aplChk As String
    Public Property Point As String

    Public Shared Function FromQuery(qs As NameValueCollection) As ApiRequest
        Return New ApiRequest With {
            .Guid = qs("guid"),
            .UID = qs("uid"),
            .ID = qs("id"),
            .Action = qs("action"),
            .Mode = qs("mode"),
            .Act = qs("act"),
            .App = qs("app"),
            .Ver = qs("ver"),
            .Time = qs("time"),
            .ApiVer = qs("api_ver"),
            .OwnerId = qs("owner_id"),
            .ModelId = qs("model_id"),
            .TableId = qs("table_id"),
            .ReqId = qs("req_id"),
            .aplChk = qs("apl_chk"),
            .Point = qs("point")
        }
    End Function
End Class
