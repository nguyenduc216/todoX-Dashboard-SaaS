namespace TodoX.Web.Services.Render;

public sealed class RenderJobPendingReconciliationException : Exception
{
    public RenderJobPendingReconciliationException(string message)
        : base(message)
    {
    }
}

public sealed class RenderJobTerminalFailureException : Exception
{
    public RenderJobTerminalFailureException(string message)
        : base(message)
    {
    }

    public RenderJobTerminalFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RenderJobDeferredException : Exception
{
    public RenderJobDeferredException(string message)
        : base(message)
    {
    }
}
