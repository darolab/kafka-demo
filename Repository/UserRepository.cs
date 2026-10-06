using Microsoft.Data.SqlClient;
using System.Data;
using KafkaDemo.Models;

namespace KafkaDemo.Repository;

public class UserRepository
{
    private readonly string _connectionString;

    public UserRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        using var connection = new SqlConnection(_connectionString);

        using var command = new SqlCommand("GetUserById", connection);

        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;

        await connection.OpenAsync();

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Email = reader.GetString(reader.GetOrdinal("Email"))
            };
        }

        return null;
    }
}