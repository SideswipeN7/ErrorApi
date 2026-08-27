using ErrorApi;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Controllers.Api.Orders;

/// <summary>
/// The same orders API, on the old-fashioned surface. Nothing here declares which errors can come
/// back: the generator finds the controller by its base class, reads the route from the attributes —
/// <c>[controller]</c> token, constraints and all — and walks each action into
/// <see cref="IOrderStore"/> exactly as it walks a Minimal API handler.
/// </summary>
/// <remarks>
/// Two return styles on purpose. <c>GetById</c> and <c>Create</c> speak MVC's own vocabulary,
/// <c>ActionResult&lt;T&gt;</c> via <c>ToActionResult()</c>; <c>Pay</c> returns <c>IResult</c> via the
/// same <c>ToHttpResult()</c> the Minimal API samples use — MVC executes those too. The problem bodies
/// are identical either way.
/// </remarks>
[ApiController]
[Route("orders")]
public sealed class OrdersController(IOrderStore store) : ControllerBase
{
    /// <summary>Reads one order.</summary>
    [HttpGet("{id:guid}")]
    public ActionResult<Order> GetById(Guid id) => store.Find(id).ToActionResult();

    /// <summary>Creates an order.</summary>
    [HttpPost]
    public ActionResult<Order> Create(CreateOrder command) =>
        store.Create(command).ToCreatedActionResult(order => $"/orders/{order.Id}");

    /// <summary>Pays an order in full.</summary>
    [HttpPost("{id:guid}/pay")]
    public IResult Pay(Guid id, decimal amount) => store.Pay(id, amount).ToHttpResult();
}
