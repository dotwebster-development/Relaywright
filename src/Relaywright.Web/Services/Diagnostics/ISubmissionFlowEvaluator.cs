using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Diagnostics;

public interface ISubmissionFlowEvaluator
{
    Task<SubmissionFlowEvaluation> EvaluateAsync(
        SubmissionFlowCheckRequest request,
        CancellationToken cancellationToken,
        SubmissionPolicy? policyOverride = null);
}
