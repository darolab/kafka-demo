
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace KafkaDemo.Controllers
{
    [ApiController]
    [Route("api/sql-users")]
    public class SqlUsersController : ControllerBase
    {
        private readonly string _connectionString;

        public SqlUsersController(IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection is not configured.");
        }

        [HttpGet]
        public IActionResult GetUsers()
        {
            var users = new List<Dictionary<string, object?>>();

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                using (var command = new SqlCommand(
                    "SELECT * FROM Users",
                    connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var user = new Dictionary<string, object?>();

                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                user[reader.GetName(i)] =
                                    reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }

                            users.Add(user);
                        }
                    }
                }
            }

            return Ok(users);
        }
    }
}