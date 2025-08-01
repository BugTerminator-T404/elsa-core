using Elsa.Workflows.Options;

namespace Elsa.Workflows;

/// <summary>
/// Invokes activities.
/// </summary>
public interface IActivityInvoker
{
    /// <summary>
    /// Invokes the specified activity.
    /// </summary>
    /// <param name="workflowExecutionContext">The workflow execution context.</param>
    /// <param name="activity">The activity to invoke.</param>
    /// <param name="options"></param>
    Task<ActivityExecutionContext> InvokeAsync(WorkflowExecutionContext workflowExecutionContext, IActivity activity, ActivityInvocationOptions? options = null);

    /// <summary>
    /// Invokes the specified activity execution context.
    /// </summary>
    Task InvokeAsync(ActivityExecutionContext activityExecutionContext);
}