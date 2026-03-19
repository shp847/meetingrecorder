namespace MeetingRecorder.Core.Services;

public sealed class SessionTitleDraftTracker
{
    private string? _sessionId;
    private string _draftTitle = string.Empty;
    private bool _isDirty;

    public string GetDisplayTitle(string? sessionId, string detectedTitle)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Clear();
            return string.Empty;
        }

        if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
        {
            _sessionId = sessionId;
            _draftTitle = detectedTitle;
            _isDirty = false;
            return _draftTitle;
        }

        if (!_isDirty && !string.Equals(_draftTitle, detectedTitle, StringComparison.Ordinal))
        {
            _draftTitle = detectedTitle;
        }

        return _draftTitle;
    }

    public void UpdateDraft(string sessionId, string detectedTitle, string draftTitle)
    {
        if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
        {
            _sessionId = sessionId;
        }

        _draftTitle = draftTitle;
        _isDirty = !string.Equals(draftTitle.Trim(), detectedTitle, StringComparison.Ordinal);
    }

    public void MarkPersisted(string sessionId, string detectedTitle)
    {
        _sessionId = sessionId;
        _draftTitle = detectedTitle;
        _isDirty = false;
    }

    public void Clear()
    {
        _sessionId = null;
        _draftTitle = string.Empty;
        _isDirty = false;
    }
}
