using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class OratorRuleService(IRepository<OratorRule> repository, ILogger<OratorRuleService> logger)
    : BaseService<OratorRule>(repository, logger)
{
    public async Task<List<OratorRule>> GetAllRulesAsync()
    {
        List<OratorRule> rules = await GetAllAsync();
        return rules.OrderBy(r => r.Order).ToList();
    }
}
