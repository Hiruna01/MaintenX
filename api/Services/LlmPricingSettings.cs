namespace CampusFacilities.Api.Services;

/// <summary>
/// What the configured LLM_MODEL costs, in US dollars per million tokens — the unit and
/// currency providers publish prices in. Read from configuration
/// (Llm:InputPricePerMillionTokensUsd / LLM_INPUT_PRICE_PER_MILLION_TOKENS_USD and the
/// output pair), never a literal in code: a price changes with the model and the provider,
/// and only a person reading the provider's page knows it.
///
/// Used by AgentMetricsService alone, to turn the tokens the provider REPORTED into an
/// ESTIMATED cost. Both prices or neither — a half-configured price would cost the prompt and
/// call the reply free. Unset is a normal state: the monitoring page then says the price is
/// not configured rather than showing a cost of zero.
/// </summary>
public class LlmPricingSettings
{
    public decimal? InputPricePerMillionTokensUsd { get; init; }

    public decimal? OutputPricePerMillionTokensUsd { get; init; }

    /// <summary>True when both prices are set — the only case in which anything is costed.</summary>
    public bool IsConfigured => InputPricePerMillionTokensUsd is not null && OutputPricePerMillionTokensUsd is not null;

    /// <summary>
    /// The estimated cost of one step's reported tokens, or null when no price is configured.
    /// decimal, like all money here, and unrounded: the caller sums before anything is shown.
    /// </summary>
    public decimal? CostOf(long promptTokens, long completionTokens) =>
        IsConfigured
            ? promptTokens * InputPricePerMillionTokensUsd!.Value / 1_000_000m
              + completionTokens * OutputPricePerMillionTokensUsd!.Value / 1_000_000m
            : null;
}
