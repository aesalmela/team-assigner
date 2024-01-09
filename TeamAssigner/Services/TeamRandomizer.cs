﻿namespace TeamAssigner.Services
{
    using System.Text;
    using System.Text.Json;
    using TeamAssigner.Models;
    public sealed class TeamRandomizer
    {
        readonly EmailService emailService;
        readonly List<PlayerInfo> players;
        readonly string weekOverride;
        readonly string baseurl;
        readonly string adminEmail;
        int year = DateTime.Now.Year;
        int week = 0;

        public TeamRandomizer(List<PlayerInfo> players, EmailService emailService, string adminEmail, string baseurl, string weekOverride = "")
        {
            try
            {
                this.baseurl = baseurl;
                this.players = players.ToList();
                this.emailService = emailService;
                this.adminEmail = adminEmail;

            }
            catch (Exception e)
            {
                weekOverride = "";
                players = [];
                Exit($"An error occurred starting the app.", true, e);
            }
        }

        public void Run()
        {
            // MAIN
            GetNFLWeek();

            if (players.Count == 16 || players.Count == 32)
            {
                Console.WriteLine($"year: {year} week: {week}\n");
                if (week > 0 && week < 19)
                {
                    StringBuilder sb = Randomize();
                    emailService.SendEmail(String.Join(",", players.Select(p => p.Email)), $"Week {week}", sb.ToString());
                }
                else
                {
                    Exit($"Not in a regular season week.", false);
                }
            }
            else
            {
                Exit($"For equal distribution of byes there must be either 16 or 32 players. There are currently {players.Count} players.", true);
            }

        }

        private void GetNFLWeek()
        {
            try
            {
                // DETERMINE NFL YEAR
                DateTime now = DateTime.Now;
                string seasonJson = RESTUtil.Get([], $"{baseurl}/seasons/{now.Year}/types/2");
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

                // DETERMINE NFL WEEK
                if (String.IsNullOrWhiteSpace(weekOverride))
                {
                    string weeksJson = RESTUtil.Get([], $"{baseurl}/seasons/{year}/types/2/weeks");
                    weeksJson = weeksJson.Replace("$ref", "reference");
                    NFLObject weeksResults = JsonSerializer.Deserialize<NFLObject>(weeksJson);
                    week = weeksResults.count;
                }
                else
                {
                    bool success = Int32.TryParse(weekOverride, out week);
                    {
                        Console.WriteLine($"Could not convert '{weekOverride}' to a number.");
                    }
                }
            }
            catch (Exception ex)
            {
                Exit($"An error occurred getting the NFL Season and Week info.", true, ex);
            }
        }

        private List<string> GetByeTeams(int week)
        {
            string weekJson = RESTUtil.Get([], $"{baseurl}/seasons/{year}/types/2/weeks/{week}");
            weekJson = weekJson.Replace("$ref", "reference");
            NFLWeekDetails weekInfo = JsonSerializer.Deserialize<NFLWeekDetails>(weekJson);

            List<string> byeTeams = [];
            if (weekInfo?.teamsOnBye != null)
            {
                foreach (var byeTeam in weekInfo.teamsOnBye)
                {
                    string byeTeamJson = RESTUtil.Get([], byeTeam.reference);
                    NFLTeam byeTeamResult = JsonSerializer.Deserialize<NFLTeam>(byeTeamJson);
                    byeTeams.Add(byeTeamResult.nickname);
                }
            }

            return byeTeams;
        }

        private int GetByeTeamCountToDate()
        {
            int count = 0;
            for (int i = week - 1; i > 0; i--)
            {
                count += GetByeTeams(i).Count;
            }

            return count;
        }

        private List<string> GetAllTeams()
        {
            var allTeams = new List<String>();
            string teamsJson = RESTUtil.Get([], $"{baseurl}/teams?limit=32");
            teamsJson = teamsJson.Replace("$ref", "reference");
            NFLObject teamsResult = JsonSerializer.Deserialize<NFLObject>(teamsJson);
            foreach (var team in teamsResult.items)
            {
                string teamJson = RESTUtil.Get([], team.reference);
                NFLTeam teamResult = JsonSerializer.Deserialize<NFLTeam>(teamJson);
                allTeams.Add(teamResult.nickname);
            }

            return allTeams;
        }

        private StringBuilder Randomize()
        {
            StringBuilder sb = new();
            try
            {
                var rnd = new Random();
                var allTeams = GetAllTeams();
                var byeTeams = GetByeTeams(week);
                byeTeams.ForEach(p => Console.WriteLine($"Bye Team: {p}"));

                //There are teams on bye so need to evenly distribute those teams
                if (byeTeams.Count != 0)
                {
                    //Setup Lists and Queues
                    Queue<string> randomNonByeTeams = new();
                    var orderedPlayers = players.OrderBy(p => p.ID);

                    var nonByeTeams = allTeams.Where(x => !byeTeams.Contains(x)).ToList();
                    nonByeTeams.OrderBy(item => rnd.Next()).Distinct().ToList().ForEach(i => randomNonByeTeams.Enqueue(i));

                    int byeMarker = GetByeTeamCountToDate();
                    Console.WriteLine($"Bye Team Count To Date: {byeMarker}\n");

                    //Assign the new bye teams to the next player ids in consecutive order
                    if (byeMarker >= 16 && players.Count <= 16)
                    {
                        byeMarker -= 16;
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
                //No bye teams so evenly distribute all teams
                else
                {
                    Queue<string> randomTeams = new Queue<string>();
                    allTeams.OrderBy(item => rnd.Next()).Distinct().ToList().ForEach(i => randomTeams.Enqueue(i));
                    var randomPlayers = players.OrderBy(item => rnd.Next());

                    foreach (var player in randomPlayers)
                    {
                        sb.AppendLine($"{player.Name}: {randomTeams.Dequeue()} & {randomTeams.Dequeue()}");
                    }
                }

                Console.WriteLine(sb.ToString());

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Exit($"An error occurred running the app.", true, ex);
            }
            return sb;
        }

        private void Exit(string msg, bool sendEmail, Exception? ex = null)
        {
            Console.WriteLine(msg);
            if (ex != null)
            {
                Console.WriteLine(ex.ToString());
            }
            if (sendEmail)
            {
                emailService.SendEmail(adminEmail, "Error Running NFL Team Assigner", $"{msg} {ex?.ToString()}");
            }
        }
    }
}