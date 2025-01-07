using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using signalR_test.Models;
using System.Diagnostics;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace signalR_test.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public IActionResult SqlTest()
        {
            return View();
        }

        [HttpPost]
        public IActionResult AddMessage([FromBody] MessageModel message)
        {
            try
            {
                _logger.LogInformation($"Adding message: {message.Message}");

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "INSERT INTO TestMessages (Message) VALUES (@Message)";
                        command.Parameters.AddWithValue("@Message", message.Message);
                        command.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Message added successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding message: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLatestData()
        {
            try
            {
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await connection.OpenAsync();
                    var query = @"
                        SELECT TOP 10
                            ChargingID, SiteID, ReceivedTime, Datatime, 
                            Sequence, PowerStatus, PcsStatus, AcbStatus,
                            Soc, kWh, PCSPower, Sitepower
                        FROM [SignalR].[dbo].[ChargingStorage]
                        ORDER BY ChargingID DESC";

                    using var command = new SqlCommand(query, connection);
                    using var reader = await command.ExecuteReaderAsync();
                    
                    var result = new List<dynamic>();
                    while (await reader.ReadAsync())
                    {
                        result.Add(new
                        {
                            ChargingID = reader.GetInt32(0),
                            SiteID = reader.GetInt32(1),
                            ReceivedTime = reader.GetDateTime(2),
                            Datatime = reader.GetDateTime(3),
                            Sequence = reader.GetInt32(4),
                            PowerStatus = reader.GetInt32(5),
                            PcsStatus = reader.GetInt32(6),
                            AcbStatus = reader.GetInt32(7),
                            Soc = reader.GetDecimal(8),
                            kWh = reader.GetDecimal(9),
                            PCSPower = reader.GetDecimal(10),
                            Sitepower = reader.GetDecimal(11)
                        });
                    }

                    return Json(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "獲取最新數據時發生錯誤");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

    public class MessageModel
    {
        public string Message { get; set; }
    }
}
