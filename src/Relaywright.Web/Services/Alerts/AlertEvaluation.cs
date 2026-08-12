namespace Relaywright.Web.Services.Alerts;

public sealed record AlertEvaluation(bool IsActive, long ObservedValue, string Message);
