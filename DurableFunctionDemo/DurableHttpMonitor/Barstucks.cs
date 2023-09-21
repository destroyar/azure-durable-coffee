using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace DurableHttpMonitor
{
    public record CoffeeIngredients(int beanWeight, int waterWeight);

    public class Barstucks
    {
        private readonly ILogger _logger;

        public Barstucks(ILogger<Barstucks> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Triggers a durable orchestration of the coffee making process with a <seealso cref="IDurableOrchestrationClient.CreateCheckStatusResponse(HttpRequestMessage, string, bool)"/>
        /// </summary>
        /// <param name="req"></param>
        /// <param name="starter"></param>
        /// <returns></returns>
        [FunctionName(nameof(MakeMeSomeCoffee))]
        public async Task<HttpResponseMessage> MakeMeSomeCoffee(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            // pull some stuff we need out of the request, just like we would w/ any other endpoint
            string bodyContent = await req.Content.ReadAsStringAsync();
            CoffeeIngredients ingredients = JsonSerializer.Deserialize<CoffeeIngredients>(bodyContent);
            
            // start the thing that does the work
            string instanceId = await starter.StartNewAsync(nameof(DoBaristaStuff), ingredients);

            // log it for...idk log stuff
            _logger.LogInformation("Started a very important complex and potentially long-running process. The orchestration ID is {instanceId}.", instanceId);

            // that's it, we just trigger the worker and wrap it with some management target URLs
            // start debugging, and run the trigger in DurableMonitor.http to see the output
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        /// <summary>
        /// <para>Performs the actual orchestration of some complex task</para>
        /// <para>"Activities" can be fired sequentially, or fanned out as necessary</para>
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        [FunctionName(nameof(DoBaristaStuff))]
        public async Task<List<string>> DoBaristaStuff(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var coffeeTimer = new Stopwatch();
            coffeeTimer.Start();

            // orchestrate the work in whatever way you need to.
            // see docs for details
            // https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp
            var prepTasks = new List<Task<string>>
            {
                context.CallActivityAsync<string>(nameof(BoilTheWater), context.GetInput<CoffeeIngredients>()),
                context.CallActivityAsync<string>(nameof(GrindTheBeans), context.GetInput<CoffeeIngredients>()),
                context.CallActivityAsync<string>(nameof(RinseTheFilter), null),
                context.CallActivityAsync<string>(nameof(PrepareChemex), null)
            };

            // wait for the prep work to be done, then make the coffee
            var outputs = (await Task.WhenAll(prepTasks)).ToList();

            // do the last bit in order
            var result = await context.CallActivityAsync<string>(nameof(PerformExtraction), null);
            outputs.Add(result);

            // observe the result of this timer
            // this orchestration is called for each of the activity tasks
            // this is also visible in the console logs
            coffeeTimer.Stop(); 
            outputs.Add($"Enjoy your coffee, I wasted {coffeeTimer.Elapsed.TotalSeconds} seconds making it.");

            return outputs.ToList();
        }

        [FunctionName(nameof(BoilTheWater))]
        public async Task<string> BoilTheWater([ActivityTrigger] CoffeeIngredients ingredients)
        {
            return await DoFakeWork($"Boiling {ingredients.waterWeight}g of water");
        }

        [FunctionName(nameof(GrindTheBeans))]
        public async Task<string> GrindTheBeans([ActivityTrigger] CoffeeIngredients ingredients)
        {
            return await DoFakeWork($"Grinding {ingredients.beanWeight}g of beans");
        }

        [FunctionName(nameof(RinseTheFilter))]
        public async Task<string> RinseTheFilter([ActivityTrigger] object unusedInput)
        {
            return await DoFakeWork("Rinsing filter");
        }

        [FunctionName(nameof(PrepareChemex))]
        public async Task<string> PrepareChemex([ActivityTrigger] object unusedInput)
        {
            return await DoFakeWork("Adding filter to chemex");
        }

        [FunctionName(nameof(PerformExtraction))]
        public async Task<string> PerformExtraction([ActivityTrigger] object unusedInput)
        {
            return await DoFakeWork("Performing extraction");
        }


        private async Task<string> DoFakeWork(string taskName)
        {
            var sw = new Stopwatch();
            sw.Start();

            var rand = new Random();
            await Task.Delay(rand.Next(1000, 10000));
            sw.Stop();

            var runLength = sw.Elapsed.TotalSeconds.ToString();

            _logger.LogInformation("{name} took {time} sec.", taskName, runLength);
            return $"Finished {taskName.ToLower()} in {runLength} sec.";
        }
    }
}
