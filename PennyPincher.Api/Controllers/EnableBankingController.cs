using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PennyPincher.Api.Extensions;
using PennyPincher.Contracts.EnableBanking;
using PennyPincher.Services.EnableBanking;

namespace PennyPincher.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/enablebanking")]
public class EnableBankingController : ErrorOrApiController
{
    private readonly IEnableBankingService _service;

    public EnableBankingController(IEnableBankingService service)
    {
        _service = service;
    }

    [HttpPost("auth/start")]
    public async Task<IActionResult> StartAuth([FromBody] StartAuthRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
            return Problem(ErrorOr.Error.Forbidden());

        var result = await _service.StartAuthAsync(userId, request, ct);
        return result.Match(r => Ok(r), errors => Problem(errors));
    }

    [HttpPost("auth/complete")]
    public async Task<IActionResult> CompleteAuth([FromBody] CompleteAuthRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
            return Problem(ErrorOr.Error.Forbidden());

        var result = await _service.CompleteAuthAsync(userId, request.Code, ct);
        return result.Match(r => Ok(r), errors => Problem(errors));
    }

    [HttpGet("accounts")]
    public IActionResult GetAccounts()
    {
        var userId = User.GetUserId();
        if (userId is null)
            return Problem(ErrorOr.Error.Forbidden());

        var result = _service.GetCachedAccounts(userId);
        return result.Match(r => Ok(r), errors => Problem(errors));
    }

    [HttpGet("accounts/{accountUid}/balances")]
    public async Task<IActionResult> GetBalances(string accountUid, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
            return Problem(ErrorOr.Error.Forbidden());

        var result = await _service.GetBalancesAsync(userId, accountUid, ct);
        return result.Match(r => Ok(r), errors => Problem(errors));
    }

    [HttpGet("accounts/{accountUid}/transactions")]
    public async Task<IActionResult> GetTransactions(string accountUid, [FromQuery] DateOnly? dateFrom, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
            return Problem(ErrorOr.Error.Forbidden());

        var from = dateFrom ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var result = await _service.GetTransactionsAsync(userId, accountUid, from, ct);
        return result.Match(r => Ok(r), errors => Problem(errors));
    }
}
