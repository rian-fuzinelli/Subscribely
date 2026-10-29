using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Contracts;

/// <summary>Trial iniciado após o provisionamento do tenant.</summary>
public sealed record SubscriptionStarted(Guid TenantId, Guid SubscriptionId, DateTimeOffset TrialEndsAt, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>Faltam 3 dias para o fim do trial (publicado uma única vez por assinatura).</summary>
public sealed record TrialEnding(Guid TenantId, Guid SubscriptionId, DateTimeOffset TrialEndsAt, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// Plano, intervalo, status ou agendamento mudou. <see cref="Change"/> descreve a transição
/// (ex.: <c>trial-converted</c>, <c>plan-upgraded</c>, <c>downgrade-scheduled</c>, <c>cancel-scheduled</c>).
/// </summary>
public sealed record SubscriptionChanged(Guid TenantId, Guid SubscriptionId, string Change, string Status, DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record SubscriptionCanceled(Guid TenantId, Guid SubscriptionId, string Reason, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>Trial terminou sem meio de pagamento.</summary>
public sealed record SubscriptionExpired(Guid TenantId, Guid SubscriptionId, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>Retentativas de pagamento esgotadas: produto bloqueado até o pagamento.</summary>
public sealed record SubscriptionSuspended(Guid TenantId, Guid SubscriptionId, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>Voltou a <c>Active</c> a partir de <c>Suspended</c>, <c>Expired</c> ou <c>Canceled</c>.</summary>
public sealed record SubscriptionReactivated(Guid TenantId, Guid SubscriptionId, DateTimeOffset OccurredAt) : IIntegrationEvent;
