﻿
using System.Net.Mail;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using TeamAssigner.Models;
using TeamAssigner.Services;
using System.Collections.Specialized;
using System.Text.Json;

AppSettings appSettings;
EmailSettings emailSettings;
List<PlayerInfo> players;
int year;
int week;

Startup(args, out appSettings, out emailSettings, out players);
GetNFLWeek(out year, out week, appSettings.WeekOverride);

if (players.Count == 16 || players.Count == 32)
{
    if (week > 0 && week < 19)
    {
        StringBuilder sb = Randomize(year, week, players);
        SendEmail(emailSettings, players, week, sb);
    }
    else
    {
        Console.WriteLine($"year: {year} week: {week}\nNot in a regular season week.");
    }
}
else
{
    Console.WriteLine($"For equal distribution of byes there must be either 16 or 32 players. There are currently {players.Count} players.");
}

static void Startup(string[] args, out AppSettings appSettings, out EmailSettings emailSettings, out List<PlayerInfo> players)
{
    var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false);
    var config = builder.Build();

    emailSettings = config.GetRequiredSection("EmailSettings").Get<EmailSettings>();
    appSettings = config.GetRequiredSection("AppSettings").Get<AppSettings>();
    players = config.GetRequiredSection("Players").Get<List<PlayerInfo>>();
}

static void GetNFLWeek(out int year, out int week, string weekOverride)
{
    // Determine Year
    RESTUtil restUtil = new();
    DateTime now = DateTime.Now;
    string seasonJson = restUtil.Get(new NameValueCollection(), $"http://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/{now.Year}/types/2");
    seasonJson = seasonJson.Replace("$ref", "reference");
    NFLSeason seasonResults = JsonSerializer.Deserialize<NFLSeason>(seasonJson);

    DateTime start = Convert.ToDateTime(seasonResults.startDate);
    DateTime end = Convert.ToDateTime(seasonResults.endDate);

    if (now.Ticks > start.Ticks)
    {
        if (now.Ticks < end.Ticks)
        {
            year = now.Year;
        }
        else
        {
            year = now.Year + 1;
        }
    }
    else
    {
        year = now.Year - 1;
    }

    // Determine Week
    if (String.IsNullOrWhiteSpace(weekOverride))
    {
        string weeksJson = restUtil.Get(new NameValueCollection(), $"http://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/{year}/types/2/weeks");
        weeksJson = weeksJson.Replace("$ref", "reference");
        NFLObject weeksResults = JsonSerializer.Deserialize<NFLObject>(weeksJson);
        week = weeksResults.count;
    }
    else
    {
        _ = Int32.TryParse(weekOverride, out week);
    }

    Console.WriteLine($"Week: {week}\n");
}

static List<string> GetByeTeams(int nflYear, int week)
{
    RESTUtil restUtil = new();
    string weekJson = restUtil.Get(new NameValueCollection(), $"http://sports.core.api.espn.com/v2/sports/football/leagues/nfl/seasons/{nflYear}/types/2/weeks/{week}");
    weekJson = weekJson.Replace("$ref", "reference");
    NFLWeekDetails weekInfo = JsonSerializer.Deserialize<NFLWeekDetails>(weekJson);

    List<string> byeTeams = new();
    if (weekInfo?.teamsOnBye != null)
    {
        foreach (var byeTeam in weekInfo.teamsOnBye)
        {
            string byeTeamJson = restUtil.Get(new NameValueCollection(), byeTeam.reference);
            NFLTeam byeTeamResult = JsonSerializer.Deserialize<NFLTeam>(byeTeamJson);
            byeTeams.Add(byeTeamResult.nickname);
        }
    }

    return byeTeams;
}

static int GetByeTeamCountToDate (int nflYear, int week)
{
    int count = 0;
    for (int i = week - 1;  i > 0; i--)
    {
        count += GetByeTeams(nflYear, i).Count;
    }

    return count;
}

static List<string> GetAllTeams()
{
    var allTeams = new List<String>();
    RESTUtil restUtil = new();
    string teamsJson = restUtil.Get(new NameValueCollection(), $"https://sports.core.api.espn.com/v2/sports/football/leagues/nfl/teams?limit=32");
    teamsJson = teamsJson.Replace("$ref", "reference");
    NFLObject teamsResult = JsonSerializer.Deserialize<NFLObject>(teamsJson);
    foreach (var team in teamsResult.items)
    {
        string teamJson = restUtil.Get(new NameValueCollection(), team.reference);
        NFLTeam teamResult = JsonSerializer.Deserialize<NFLTeam>(teamJson);
        allTeams.Add(teamResult.nickname);
    }

    return allTeams;
}

static StringBuilder Randomize(int year, int currentWeek, List<PlayerInfo> players)
{
    StringBuilder sb = new();
    var rnd = new Random();

    var allTeams = GetAllTeams();


    var byeTeams = GetByeTeams(year, currentWeek);
    byeTeams.ForEach(p => Console.WriteLine($"Bye Team: {p}"));
    if (byeTeams.Any())
    {
        //There are teams on bye so need to evenly distribute those teams

        //Setup Lists and Queues
        Queue<string> randomNonByeTeams = new();
        var orderedPlayers = players.OrderBy(p => p.ID);

        var nonByeTeams = allTeams.Where(x => !byeTeams.Contains(x)).ToList();
        nonByeTeams.OrderBy(item => rnd.Next()).Distinct().ToList().ForEach(i => randomNonByeTeams.Enqueue(i));

        int byeMarker = GetByeTeamCountToDate(year, currentWeek);
        Console.WriteLine($"Bye Team Count To Date: {byeMarker}\n");

        //Assign the new bye teams to the next player ids in consecutive order
        if (byeMarker >= 16 && players.Count <= 16)
        {
            byeMarker = byeMarker - 16;
            Console.WriteLine($"Bye Team Marker Set To: {byeMarker}\n");
        }
        foreach (var byeTeamName in byeTeams)
        {
            byeMarker++;

            var player = orderedPlayers.Where(p => p.ID.Equals(byeMarker)).First();
            sb.AppendLine($"{player.Name}: {byeTeamName} & {randomNonByeTeams.Dequeue()}");

            player.Filled = true;
        }

        //Fill out the rest of the teams
        foreach (var player in orderedPlayers)
        {
            if (!player.Filled)
            {
                sb.AppendLine($"{player.Name}: {randomNonByeTeams.Dequeue()} & {randomNonByeTeams.Dequeue()}");
            }
        }
    }
    else
    {
        //No bye teams so evenly distribute all teams
        Queue<string> randomTeams = new Queue<string>();
        allTeams.OrderBy(item => rnd.Next()).Distinct().ToList().ForEach(i => randomTeams.Enqueue(i));
        var randomPlayers = players.OrderBy(item => rnd.Next());

        foreach (var player in randomPlayers)
        {
            sb.AppendLine($"{player.Name}: {randomTeams.Dequeue()} & {randomTeams.Dequeue()}");
        }
    }

    Console.WriteLine(sb.ToString());
    return sb;
}

static void SendEmail(EmailSettings emailSettings, List<PlayerInfo> players, int week, StringBuilder sb)
{
    var client = new SmtpClient(emailSettings.SMTPServer, emailSettings.SMTPPort)
    {
        Credentials = new NetworkCredential(emailSettings.FromEmail, emailSettings.Psswd),
        EnableSsl = true
    };
    client.Send(emailSettings.FromEmail, String.Join(",", players.Select(p => p.Email)), $"Week {week}", sb.ToString());
}