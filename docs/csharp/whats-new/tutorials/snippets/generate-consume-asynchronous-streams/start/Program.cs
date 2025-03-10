﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;

const string PagedIssueQuery =
@"query ($repo_name: String!,  $start_cursor:String) {
repository(owner: ""dotnet"", name: $repo_name) {
issues(last: 25, before: $start_cursor)
 {
    totalCount
    pageInfo {
      hasPreviousPage
      startCursor
    }
    nodes {
      title
      number
      createdAt
    }
  }
}
}
";

//Follow these steps to create a GitHub Access Token
// https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line/#creating-a-token
//Select the following permissions for your GitHub Access Token:
// - repo:status
// - public_repo
// Replace the 3rd parameter to the following code with your GitHub access token.
var key = GetEnvVariable("GitHubKey",
"You must store your GitHub key in the 'GitHubKey' environment variable",
"");

var client = new GitHubClient(new Octokit.ProductHeaderValue("IssueQueryDemo"))
{
    Credentials = new Octokit.Credentials(key)
};

int num = 0;
var cancellation = new CancellationTokenSource();
await foreach (var issue in runPagedQueryAsync(client, PagedIssueQuery, "docs").WithCancellation(cancellation.Token))
{
    Console.WriteLine(issue);
    Console.WriteLine($"Received {++num} issues in total");
}


static async IAsyncEnumerable<JToken> runPagedQueryAsync(GitHubClient client, string queryText, string repoName, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var issueAndPRQuery = new GraphQLRequest
    {
        Query = queryText
    };
    issueAndPRQuery.Variables["repo_name"] = repoName;
    
    bool hasMorePages = true;
    int pagesReturned = 0;

    // Stop with 10 pages, because these are large repos:
    while (hasMorePages && (pagesReturned++ < 10))
    {
        var postBody = issueAndPRQuery.ToJsonText();
        var response = await client.Connection.Post<string>(new Uri("https://api.github.com/graphql"),
            postBody, "application/json", "application/json");

        JObject results = JObject.Parse(response.HttpResponse.Body.ToString());

        hasMorePages = (bool)pageInfo(results)["hasPreviousPage"];
        issueAndPRQuery.Variables["start_cursor"] = pageInfo(results)["startCursor"].ToString();
        foreach (var issue in issues(results)["nodes"])
        {
            yield return issue;
        }
    }

    static JObject issues(JObject result) => (JObject)result["data"]["repository"]["issues"];
    static JObject pageInfo(JObject result) => (JObject)issues(result)["pageInfo"];
}

static string GetEnvVariable(string item, string error, string defaultValue)
{
    var value = Environment.GetEnvironmentVariable(item);
    if (string.IsNullOrWhiteSpace(value))
    {
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            return defaultValue;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.WriteLine(error);
            Environment.Exit(0);
        }
    }
    return value;
}

public class GraphQLRequest
{
    [JsonProperty("query")]
    public string Query { get; set; }

    [JsonProperty("variables")]
    public IDictionary<string, object> Variables { get; } = new Dictionary<string, object>();

    public string ToJsonText() => JsonConvert.SerializeObject(this);
}
