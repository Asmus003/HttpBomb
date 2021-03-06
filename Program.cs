﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HttpBomb.OptionValidators;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Validation;

namespace HttpBomb
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication(){
                Name = "HttpBomb",
                Description = "Http bombarding tool. Show no mercy."
            };
            app.HelpOption();

            var threadsOption = app.Option<int>("-n|--number <N>", "[Required] Number of threads to run", CommandOptionType.SingleValue);
            threadsOption.Validators.Add(new ThreadValidator());

            var durationOption = app.Option<int>("-d|--duration <SECONDS>", "[Required] Time to run in seconds", CommandOptionType.SingleValue);
            durationOption.Validators.Add(new DurationValidator());

            var urlOption = app.Option("-u|--url <URL>", "[Required] Url to hit", CommandOptionType.SingleValue);
            urlOption.Validators.Add(new UrlValidator());

            var timeoutOption = app.Option<int>("-t|--timeout <SECONDS>", "[Optional] Http client timeout in seconds", CommandOptionType.SingleValue);
            Func<int?> getTimeoutValue = () => timeoutOption.HasValue()? new Nullable<int>(timeoutOption.ParsedValue) : null;

            app.OnExecute(() => Execute(threadsOption.ParsedValue, durationOption.ParsedValue, urlOption.Value(), getTimeoutValue()));

            app.Execute(args);
        }

        static void Execute(int threads, int duration, string url, int? timeout)
        {
            long successCount = 0;
            long failCount = 0;

            Action incrementSuccess = () => Interlocked.Increment(ref successCount);
            Action incrementFail = () => Interlocked.Increment(ref failCount);

            Func<long> getSuccessCount = () => successCount;
            Func<long> getFailCount = () => failCount;
            
            var stopWatch = Stopwatch.StartNew();

            var client = new HttpClient();
            if (timeout.HasValue)
                client.Timeout = TimeSpan.FromSeconds(timeout.Value);

            Enumerable.Range(0, threads)
                .ToList()
                .ForEach(_ => Task.Run(async () => await MainThread(client, url, incrementSuccess, incrementFail)));
            Task.Run(() => CounterThread(stopWatch, getSuccessCount, getFailCount));
            Thread.Sleep(TimeSpan.FromSeconds(duration));

            stopWatch.Stop();
            ShowResult(stopWatch, successCount, failCount);
        }

        static async Task MainThread(HttpClient client, string url, Action onSuccess, Action onFail)
        {
            while (true)
                await client.GetAsync(url).ContinueWith(t => {
                    if (t.IsCanceled || t.Result.StatusCode != HttpStatusCode.OK)
                        onFail();
                    else
                        onSuccess();
                });
        }

        static void CounterThread(Stopwatch sw, Func<long> getSuccessCount, Func<long> getFailCount)
        {
            long lastSuccessCount = 0;
            double lastSwTime = 0;
          
            while(true){
                var swTime = sw.Elapsed.TotalSeconds;
                var successCount = getSuccessCount();
                var failCount = getFailCount();
                var successRate  = Math.Round((successCount - lastSuccessCount) / (swTime - lastSwTime), 2);
                lastSuccessCount = successCount;
                lastSwTime = swTime;
                
                Console.WriteLine($"Success count: {successCount}, Fail count: {failCount}, Success/sec: {successRate} Req/sec");
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        private static void ShowResult(Stopwatch sw, long successCount, long failCount)
        {
            Console.WriteLine("");
            Console.WriteLine($"Results: Total success: {successCount}, Total fail: {failCount}, Average success/second: {successCount/sw.Elapsed.TotalSeconds}");
        }
    }
}
