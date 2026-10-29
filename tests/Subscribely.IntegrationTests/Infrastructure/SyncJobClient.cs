using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>
/// Substitui o cliente do Hangfire nos testes: jobs enfileirados rodam na hora (num escopo de DI novo);
/// jobs agendados ficam guardados até <see cref="RunDueAsync"/>. Falhas são registradas, não propagadas.
/// </summary>
public sealed class SyncJobClient(IServiceScopeFactory scopes) : IBackgroundJobClient
{
    private readonly List<(Job Job, DateTimeOffset RunAt)> _scheduled = [];

    public List<Exception> Failures { get; } = [];

    public string Create(Job job, IState state)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (state is ScheduledState scheduled)
        {
            lock (_scheduled)
            {
                _scheduled.Add((job, scheduled.EnqueueAt));
            }
        }
        else
        {
            Execute(job);
        }

        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;

    /// <summary>Executa os jobs agendados cujo horário já chegou.</summary>
    public Task RunDueAsync(DateTimeOffset now)
    {
        List<Job> due;
        lock (_scheduled)
        {
            due = _scheduled.Where(s => s.RunAt <= now).Select(s => s.Job).ToList();
            _scheduled.RemoveAll(s => s.RunAt <= now);
        }

        due.ForEach(Execute);
        return Task.CompletedTask;
    }

    private void Execute(Job job)
    {
        using var scope = scopes.CreateScope();
        var instance = job.Method.IsStatic ? null : ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, job.Type);
        var args = job.Args.Select(a => a is CancellationToken ? CancellationToken.None : a).ToArray();
        try
        {
            if (job.Method.Invoke(instance, args) is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            lock (Failures)
            {
                Failures.Add(ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex);
            }
        }
    }
}
