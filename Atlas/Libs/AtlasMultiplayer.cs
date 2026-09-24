using System.Collections.Generic;
using ManiaScriptSharp;
using Atlas.Libs;
#nullable disable

namespace Atlas.Libs;

/// <summary>HTTP session client for Atlas editor operations.</summary>
public class AtlasMultiplayer : CMapEditorPlugin, ILib
{
    private readonly AtlasEngine Atlas = new();
    public TextLib TextLib;

    public struct ServerResponse
    {
        public int StatusCode;
        public string Body;
        public string OperationId;
    }

    public struct IncomingEvent
    {
        public int Sequence;
        public string OperationId;
        public string AuthorClientId;
        public string Kind;
        public string Body;
    }

    private string serverUrl = "";
    private string sessionId = "";
    private string clientId = "";
    private string pendingOperationId = "";
    private string pendingOperationBody = "";
    private int revision;
    private int pendingRevision;
    private int pendingEventCount;
    private bool awaitingApply;
    private bool awaitingSnapshot;
    private CHttpRequest joinRequest;
    private CHttpRequest pollRequest;
    private CHttpRequest operationRequest;
    private readonly List<ServerResponse> batches = [];
    private readonly List<ServerResponse> operations = [];
    private readonly List<ServerResponse> errors = [];
    private readonly List<IncomingEvent> events = [];

    public string ClientId => clientId;
    public int Revision => revision;
    public bool IsJoined => clientId != "";
    public bool AwaitingApply => awaitingApply;
    public bool AwaitingSnapshot => awaitingSnapshot;
    public string PendingOperationId => pendingOperationId;

    public IList<ServerResponse> Batches
    {
        get
        {
            var result = new List<ServerResponse>();
            foreach (var entry in batches) result.Add(entry);
            batches.Clear();
            return result;
        }
    }

    public IList<ServerResponse> OperationResponses
    {
        get
        {
            var result = new List<ServerResponse>();
            foreach (var entry in operations) result.Add(entry);
            operations.Clear();
            return result;
        }
    }

    public IList<ServerResponse> Errors
    {
        get
        {
            var result = new List<ServerResponse>();
            foreach (var entry in errors) result.Add(entry);
            errors.Clear();
            return result;
        }
    }

    public IList<IncomingEvent> Events
    {
        get
        {
            var result = new List<IncomingEvent>();
            foreach (var entry in events) result.Add(entry);
            events.Clear();
            return result;
        }
    }

    /// <summary>Extract top-level event objects without splitting nested change arrays.</summary>
    public static List<string> SplitEvents(string json)
    {
        var result = new List<string>();
        var marker = -1;
        for (var i = 0; i + 8 <= json.Length; i++)
            if (json.Substring(i, 8) == "\"events\"") { marker = i + 8; break; }
        if (marker < 0) return result;
        var arrayStart = -1;
        for (var i = marker; i < json.Length; i++)
            if (json.Substring(i, 1) == "[") { arrayStart = i + 1; break; }
        if (arrayStart < 0) return result;
        var depth = 0;
        var objectStart = -1;
        var quoted = false;
        var escaped = false;
        for (var i = arrayStart; i < json.Length; i++)
        {
            var ch = json.Substring(i, 1);
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (ch == "\\") escaped = true;
                else if (ch == "\"") quoted = false;
                continue;
            }
            if (ch == "\"") { quoted = true; continue; }
            if (ch == "{" )
            {
                if (depth == 0) objectStart = i;
                depth++;
            }
            else if (ch == "}")
            {
                depth--;
                if (depth == 0 && objectStart >= 0)
                {
                    result.Add(json.Substring(objectStart, i - objectStart + 1));
                    objectStart = -1;
                }
            }
            else if (ch == "]" && depth == 0) break;
        }
        return result;
    }

    public void Configure(string baseUrl, string session)
    {
        serverUrl = baseUrl;
        if (TextLib.EndsWith("/", serverUrl)) serverUrl = serverUrl.Substring(0, serverUrl.Length - 1);
        sessionId = session;
    }

    private string SessionUrl() => serverUrl + "/sessions/" + TextLib.URLEncode(sessionId);

    private string EscapeJson(string value) => value.Replace("\\", "\\\\")
        .Replace("\"", "\\\"").Replace("\n", "\\n");

    private string JsonString(string json, string field)
    {
        var match = TextLib.RegexMatch("\"" + field + "\"\\s*:\\s*\"([^\"]*)\"", json, "");
        if (match.Count < 2) return "";
        return match[1];
    }

    private int JsonInteger(string json, string field)
    {
        var match = TextLib.RegexMatch("\"" + field + "\"\\s*:\\s*([0-9]+)", json, "");
        if (match.Count < 2) return -1;
        return TextLib.ToInteger(match[1]);
    }

    public bool Join(string displayName)
    {
        if (serverUrl == "" || sessionId == "" || joinRequest != null || IsJoined || Http.SlotsAvailable < 1)
            return false;
        var body = "{\"displayName\":\"" + EscapeJson(displayName) + "\"}";
        joinRequest = Http.CreatePost(SessionUrl() + "/join", body, "Content-Type: application/json");
        return joinRequest != null;
    }

    /// <summary>Payload is immutable operation intent encoded as a JSON object.</summary>
    public bool SubmitIntent(string operationId, string kind, string payloadJson)
    {
        if (!IsJoined || awaitingApply || awaitingSnapshot || operationId == "" || kind == "" || pendingOperationId != "" ||
            payloadJson == "" || Http.SlotsAvailable < 1) return false;
        pendingOperationId = operationId;
        pendingOperationBody = "{\"operationId\":\"" + EscapeJson(operationId) +
            "\",\"clientId\":\"" + EscapeJson(clientId) + "\",\"baseRevision\":" +
            revision + ",\"kind\":\"" + EscapeJson(kind) + "\",\"payload\":" + payloadJson + "}";
        operationRequest = Http.CreatePost(SessionUrl() + "/operations", pendingOperationBody,
            "Content-Type: application/json");
        return operationRequest != null;
    }

    /// <summary>Retry the same operation id and body after a transport failure.</summary>
    public bool RetryPendingOperation()
    {
        if (pendingOperationId == "" || operationRequest != null || Http.SlotsAvailable < 1) return false;
        operationRequest = Http.CreatePost(SessionUrl() + "/operations", pendingOperationBody,
            "Content-Type: application/json");
        return operationRequest != null;
    }

    public void Update()
    {
        if (joinRequest != null && joinRequest.IsCompleted)
        {
            var status = joinRequest.StatusCode;
            var body = joinRequest.Result;
            Http.Destroy(joinRequest);
            joinRequest = null;
            if (status == 200)
            {
                clientId = JsonString(body, "clientId");
                revision = JsonInteger(body, "revision");
                if (clientId == "" || revision < 0)
                {
                    clientId = "";
                    errors.Add(new ServerResponse { StatusCode = status, Body = body });
                }
                else
                {
                    awaitingSnapshot = true;
                    batches.Add(new ServerResponse { StatusCode = status, Body = body });
                }
            }
            else errors.Add(new ServerResponse { StatusCode = status, Body = body });
        }
        if (operationRequest != null && operationRequest.IsCompleted)
        {
            var status = operationRequest.StatusCode;
            var body = operationRequest.Result;
            Http.Destroy(operationRequest);
            operationRequest = null;
            var response = new ServerResponse { StatusCode = status, Body = body, OperationId = pendingOperationId };
            if (status == 202 || status == 409 || status == 422)
            {
                operations.Add(response);
                pendingOperationId = "";
                pendingOperationBody = "";
            }
            else errors.Add(response);
        }
        if (pollRequest != null && pollRequest.IsCompleted)
        {
            var status = pollRequest.StatusCode;
            var body = pollRequest.Result;
            Http.Destroy(pollRequest);
            pollRequest = null;
            if (status == 200)
            {
                pendingRevision = JsonInteger(body, "revision");
                if (pendingRevision < revision) errors.Add(new ServerResponse { StatusCode = status, Body = body });
                else
                {
                    var bodies = SplitEvents(body);
                    var expected = revision + 1;
                    var valid = true;
                    foreach (var eventBody in bodies)
                    {
                        if (JsonInteger(eventBody, "sequence") != expected) valid = false;
                        expected++;
                    }
                    if (!valid || (bodies.Count == 0 && pendingRevision != revision) ||
                        (bodies.Count > 0 && pendingRevision != expected - 1))
                        errors.Add(new ServerResponse { StatusCode = status, Body = body });
                    else if (bodies.Count > 0)
                    {
                        pendingEventCount = bodies.Count;
                        awaitingApply = true;
                        foreach (var eventBody in bodies)
                            events.Add(new IncomingEvent { Sequence = JsonInteger(eventBody, "sequence"),
                                OperationId = JsonString(eventBody, "operationId"),
                                AuthorClientId = JsonString(eventBody, "authorClientId"),
                                Kind = JsonString(eventBody, "kind"), Body = eventBody });
                        batches.Add(new ServerResponse { StatusCode = status, Body = body });
                    }
                }
            }
            else if (status == 410)
            {
                awaitingSnapshot = true;
                batches.Add(new ServerResponse { StatusCode = status, Body = body });
            }
            else errors.Add(new ServerResponse { StatusCode = status, Body = body });
        }
        if (IsJoined && !awaitingApply && !awaitingSnapshot && pollRequest == null && Http.SlotsAvailable > 0)
        {
            var url = SessionUrl() + "/events?clientId=" + TextLib.URLEncode(clientId) +
                "&after=" + revision + "&waitSeconds=20";
            pollRequest = Http.CreateGet(url, false);
        }
    }

    /// <summary>Call only after applying every event through sequence to the map.</summary>
    public bool AcknowledgeApplied(int sequence)
    {
        if (!awaitingApply || sequence != revision + 1 || sequence > pendingRevision) return false;
        revision = sequence;
        pendingEventCount--;
        if (pendingEventCount == 0) awaitingApply = false;
        return true;
    }

    /// <summary>Apply parsed authoritative changes and advance the polling cursor together.</summary>
    public bool ApplyBatch(int sequence, IList<AtlasEngine.ItemBlock> removed,
        IList<AtlasEngine.Placement> placed, IList<Int3> removedWater)
    {
        if (!awaitingApply || sequence != revision + 1) return false;
        if (!Atlas.ApplyResolvedChanges(removed, placed, removedWater)) return false;
        return AcknowledgeApplied(sequence);
    }

    /// <summary>Replace Atlas metadata from the 410 snapshot before polling again.</summary>
    public bool ApplySnapshot(int snapshotRevision, IList<AtlasEngine.ItemBlock> itemBlocks, IList<Int3> removedWater)
    {
        if (!awaitingSnapshot || snapshotRevision < 0) return false;
        Atlas.SetItemBlockList(itemBlocks);
        Atlas.SetRemovedWater(removedWater);
        revision = snapshotRevision;
        awaitingSnapshot = false;
        return true;
    }
}
