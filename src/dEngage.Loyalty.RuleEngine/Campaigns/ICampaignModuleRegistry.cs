namespace dEngage.Loyalty.RuleEngine.Campaigns;

public interface ICampaignModuleRegistry
{
    // A CachedCampaignConfig's CampaignType with no registered module is a configuration/
    // deployment bug (the cache loader only ever produces types a module should exist for),
    // not untrusted input — this throws rather than silently no-opping.
    ICampaignModule Resolve(string campaignType);
}
