using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // API endpoint
        string url = "https://ark.cn-beijing.volces.com/api/v3/responses";
        
        // Bearer token
        string token = "3081e60c-5412-4feb-91b4-fd1dca96f4c1";
        
        // JSON payload
        string jsonPayload = @"{
            ""model"": ""doubao-seed-1-6-flash-250828"",
            ""input"": [
                {
                    ""role"": ""user"",
                    ""content"": [
                        {
                            ""type"": ""input_image"",
                            ""image_url"": ""https://ark-project.tos-cn-beijing.volces.com/doc_image/ark_demo_img_1.png""
                        },
                        {
                            ""type"": ""input_text"",
                            ""text"": ""你看见了什么？""
                        }
                    ]
                }
            ]
        }";
        
        try
        {
            using (HttpClient client = new HttpClient())
            {
                // Set headers
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                
                // Create request content
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // Send POST request
                HttpResponseMessage response = await client.PostAsync(url, content);
                
                // Read response
                string responseBody = await response.Content.ReadAsStringAsync();
                
                // Display results
                Console.WriteLine("Status Code: " + response.StatusCode);
                Console.WriteLine("Response Body: ");
                Console.WriteLine(responseBody);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
