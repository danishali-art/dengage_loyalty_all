namespace dEngage.Loyalty.RuleEngine.Campaigns;

public sealed class CampaignModuleRegistry : ICampaignModuleRegistry
{
    private readonly Dictionary<string, ICampaignModule> _modules;

    public CampaignModuleRegistry(IEnumerable<ICampaignModule> modules)
    {
        _modules = modules.ToDictionary(m => m.CampaignType);
    }

    public ICampaignModule Resolve(string campaignType) =>
        _modules.TryGetValue(campaignType, out var module)
            ? module
            : throw new InvalidOperationException($"no campaign module registered for campaign_type '{campaignType}'");
}
