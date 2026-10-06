using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KafkaDemo.Data;
using KafkaDemo.Models;
using Microsoft.AspNetCore.Authorization;
using KafkaDemo.DTO;
using KafkaDemo.Repository;

namespace KafkaDemo.Controllers;
[ApiController]
// [Authorize]
[ServiceFilter(typeof(ControllerAspect))]
[Route("api/[controller]")]
public class UserController : ControllerBase
{

     private readonly ILogger<UserController> _logger;
     private readonly UserRepository _userRepository;


    private readonly AppDbContext _context;

    public UserController(AppDbContext context, ILogger<UserController> logger, UserRepository userRepository)
    {
        _context = context;
       _logger = logger;
       _userRepository = userRepository;
    }

    // GET: api/user
    [HttpGet]
    public async Task<ActionResult<IEnumerable<User>>> GetUsers()
    {
        var users = await _context.Users.ToListAsync();
        _logger.LogInformation("*********************");

        return Ok(users);
    }

    // GET: api/user/1
    [HttpGet("{id}")]
    public async Task<ActionResult<User>> GetUser(int id)
    {
        var user = await _context.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    // POST: api/user
    [HttpPost]
    public async Task<ActionResult<User>> CreateUser(CreateUserRequestDTO request)


    {

        var user = new User
        {
            Name = request.FullName,
            Email = request.Email,
            Age = request.Age,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Ok(user);


        //User SaveUser = new User
        //{
        //    Name = user.FullName,
        //    Email = user.Email,
        //    Age = user.Age,
        //    CreatedAt = DateTime.UtcNow
        //};



        //_context.Users.Add(SaveUser);

        //await _context.SaveChangesAsync();

        //return CreatedAtAction(
        //    nameof(GetUser),
        //    user
        //);
    }

    // PUT: api/user/1
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, User user)
    {
        if (id != user.Id)
        {
            return BadRequest();
        }

        _context.Entry(user).State = EntityState.Modified;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE: api/user/1
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _context.Users.FindAsync(id);

        if (user == null)
        {

            return NotFound();
        }

        _context.Users.Remove(user);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("/{id}")]
    public async Task<IActionResult> GetUserByProceduce(int id)
    {
        var user = await _userRepository.GetUserByIdAsync(id);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    // GET: api/user/search?q=alice
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<User>>> SearchUsers([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("The q query parameter is required.");
        }

        var pattern = $"%{q.Trim()}%";
        var users = await _context.Users
            .AsNoTracking()
            .Where(user =>
                EF.Functions.Like(user.Name, pattern) ||
                EF.Functions.Like(user.Email, pattern))
            .ToListAsync();

        return Ok(users);
    }
}