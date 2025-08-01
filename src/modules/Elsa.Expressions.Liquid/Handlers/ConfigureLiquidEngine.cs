using System.Dynamic;
using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Expressions.Liquid.Helpers;
using Elsa.Expressions.Liquid.Notifications;
using Elsa.Expressions.Liquid.Options;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Memory;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.Liquid.Handlers;

/// <summary>
/// Configures the liquid templating engine before evaluating a liquid expression.
/// </summary>
internal class ConfigureLiquidEngine : INotificationHandler<RenderingLiquidTemplate>
{
    private readonly IConfiguration _configuration;
    private readonly ManagementOptions _managementOptions;
    private readonly FluidOptions _fluidOptions;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ConfigureLiquidEngine(IConfiguration configuration, IOptions<FluidOptions> fluidOptions, IOptions<ManagementOptions> managementOptions)
    {
        _configuration = configuration;
        _managementOptions = managementOptions.Value;
        _fluidOptions = fluidOptions.Value;
    }

    /// <inheritdoc />
    public Task HandleAsync(RenderingLiquidTemplate notification, CancellationToken cancellationToken)
    {
        var context = notification.TemplateContext;
        var options = context.Options;
        var memberAccessStrategy = options.MemberAccessStrategy;

        memberAccessStrategy.Register<ExpandoObject>();
        memberAccessStrategy.Register<LiquidPropertyAccessor, FluidValue>((x, name) => x.GetValueAsync(name));
        memberAccessStrategy.Register<ExpandoObject, object>((x, name) => ((IDictionary<string, object>)x!)[name]);
        memberAccessStrategy.Register<ExpressionExecutionContext, LiquidPropertyAccessor>("Variables", x => new LiquidPropertyAccessor(name => GetVariable(x, name, options)));
        memberAccessStrategy.Register<ExpressionExecutionContext, LiquidPropertyAccessor>("Input", x => new LiquidPropertyAccessor(name => GetInput(x, name, options)));
        memberAccessStrategy.Register<ExpressionExecutionContext, string?>("CorrelationId", x => x.GetWorkflowExecutionContext().CorrelationId);
        memberAccessStrategy.Register<ExpressionExecutionContext, string>("WorkflowDefinitionId", x => x.GetWorkflowExecutionContext().Workflow.Identity.DefinitionId);
        memberAccessStrategy.Register<ExpressionExecutionContext, string>("WorkflowDefinitionVersionId", x => x.GetWorkflowExecutionContext().Workflow.Identity.Id);
        memberAccessStrategy.Register<ExpressionExecutionContext, int>("WorkflowDefinitionVersion", x => x.GetWorkflowExecutionContext().Workflow.Identity.Version);
        memberAccessStrategy.Register<ExpressionExecutionContext, string>("WorkflowInstanceId", x => x.GetActivityExecutionContext().WorkflowExecutionContext.Id);

        if (_fluidOptions.AllowConfigurationAccess)
        {
            memberAccessStrategy.Register<ExpressionExecutionContext, LiquidPropertyAccessor>("Configuration", x => new LiquidPropertyAccessor(name => ToFluidValue(GetConfigurationValue(name), options)));
            memberAccessStrategy.Register<ConfigurationSectionWrapper, ConfigurationSectionWrapper?>((source, name) => source.GetSection(name));
        }

        // Register all variable types.
        foreach (var variableDescriptor in _managementOptions.VariableDescriptors.Where(x => x.Type is { IsClass: true, ContainsGenericParameters: false }))
            memberAccessStrategy.Register(variableDescriptor.Type);

        return Task.CompletedTask;
    }

    private ConfigurationSectionWrapper GetConfigurationValue(string name) => new(_configuration.GetSection(name));
    private Task<FluidValue> ToFluidValue(object? input, TemplateOptions options) => Task.FromResult(FluidValue.Create(input, options));

    private Task<FluidValue> GetVariable(ExpressionExecutionContext context, string key, TemplateOptions options)
    {
        var value = GetVariableInScope(context, key);
        return Task.FromResult(value == null ? NilValue.Instance : FluidValue.Create(value, options));
    }

    private Task<FluidValue> GetInput(ExpressionExecutionContext context, string key, TemplateOptions options)
    {
        // First, check if the current activity has inputs
        if (context.TryGetActivityExecutionContext(out var activityExecutionContext) &&
            activityExecutionContext.ActivityInput.TryGetValue(key, out var activityValue))
        {
            return Task.FromResult(activityValue == null ? NilValue.Instance : FluidValue.Create(activityValue, options));
        }
        
        // Fall back to workflow inputs if activity inputs don't contain the key
        var workflowExecutionContext = context.GetWorkflowExecutionContext();
        var input = workflowExecutionContext.Input.TryGetValue(key, out var workflowValue) ? workflowValue : default;
        
        return Task.FromResult(input == null ? NilValue.Instance : FluidValue.Create(workflowValue, options));
    }

    private static object? GetVariableInScope(ExpressionExecutionContext context, string variableName)
    {
        var q = from variable in context.EnumerateVariablesInScope()
            where variable.Name == variableName
            where variable.TryGet(context, out _)
            select variable.Get(context);

        return q.FirstOrDefault();
    }
}