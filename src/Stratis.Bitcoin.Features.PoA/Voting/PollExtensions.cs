using System.Collections.Generic;
using System.Linq;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public static class PollExtensions
    {
        public static List<Poll> MemberPollsOnly(this List<Poll> polls)
        {
            return polls.Where(p => p.VotingData.Key == VoteKey.AddFederationMember || p.VotingData.Key == VoteKey.KickFederationMember).ToList();
        }
    }
}