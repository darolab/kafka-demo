using KafkaDemo.Models;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace KafkaDemo.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly PostApiClient _postApiClient;

    public PostsController(PostApiClient postApiClient)
    {
        _postApiClient = postApiClient;
    }

    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var posts = await _postApiClient.GetPosts();

        return Ok(posts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPost(int id)
    {
        var post = await _postApiClient.GetPostById(id);

        if (post == null)
        {
            return NotFound();
        }

        return Ok(post);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost(Post post)
    {
        var result = await _postApiClient.CreatePost(post);

        return Ok(result);
    }
}