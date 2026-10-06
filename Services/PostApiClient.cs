namespace KafkaDemo.Services;
using System.Net.Http.Json;
using KafkaDemo.Models;

public class PostApiClient
{
    private readonly HttpClient _httpClient;
    public PostApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        this._httpClient = httpClient;
        var baseUrl = configuration["ExternalApis:UserApi"];
        _httpClient.BaseAddress = new Uri(baseUrl!);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<List<Post>> GetPosts()
    {

        var posts = await _httpClient.GetFromJsonAsync<List<Post>>(
            "/posts"
        );

        return posts ?? new List<Post>();
        
    }

     public async Task<Post?> GetPostById(int id)
    {
        return await _httpClient.GetFromJsonAsync<Post>(
            $"/posts/{id}"
        );
    }

    public async Task<Post?> CreatePost(Post post)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/posts",
            post
        );

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Post>();
    }
}