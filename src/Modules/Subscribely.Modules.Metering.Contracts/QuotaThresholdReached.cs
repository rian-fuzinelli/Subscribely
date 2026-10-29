using Subscribely.SharedKernel;

namespace Subscribely.Modules.Metering.Contracts;

/// <summary>O uso do mês cruzou 80% ou 100% da quota (publicado uma única vez por threshold e período).</summary>
public sealed record QuotaThresholdReached(Guid TenantId, string Period, int Threshold, long Limit, DateTimeOffset OccurredAt) : IIntegrationEvent;
