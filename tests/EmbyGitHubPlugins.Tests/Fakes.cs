using System.Net;
using System.Text;
using MediaBrowser.Model.Logging;

namespace EmbyGitHubPlugins.Tests;

/// <summary>Fake Radarr: canned responses per "METHOD /path" (query string ignored), records every request with its body.</summary>
internal sealed class FakeRadarr : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new();

    public Exception? Throw { get; set; }

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public void On(string methodAndPath, string body, HttpStatusCode status = HttpStatusCode.OK)
        => this.Responses[methodAndPath] = (status, body);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        this.Requests.Add((request, body));
        if (this.Throw != null)
        {
            throw this.Throw;
        }

        var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath;
        var (status, responseBody) = this.Responses.TryGetValue(key, out var r) ? r : (HttpStatusCode.NotFound, "");
        return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Records every formatted log line so tests can check the API key never leaks.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<string> Lines { get; } = new();

    private void Add(string message, object[] args, Exception? ex = null)
        => this.Lines.Add((args.Length > 0 ? string.Format(message, args) : message) + (ex != null ? " " + ex : ""));

    public void Info(string message, params object[] paramList) => this.Add(message, paramList);
    public void Error(string message, params object[] paramList) => this.Add(message, paramList);
    public void Warn(string message, params object[] paramList) => this.Add(message, paramList);
    public void Debug(string message, params object[] paramList) => this.Add(message, paramList);
    public void Fatal(string message, params object[] paramList) => this.Add(message, paramList);
    public void FatalException(string message, Exception exception, params object[] paramList) => this.Add(message, paramList, exception);
    public void ErrorException(string message, Exception exception, params object[] paramList) => this.Add(message, paramList, exception);
    public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent) => this.Lines.Add(message + additionalContent);
    public void Log(LogSeverity severity, string message, params object[] paramList) => this.Add(message, paramList);
    public void Log(LogSeverity severity, ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Error(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Warn(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Info(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Debug(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
}
