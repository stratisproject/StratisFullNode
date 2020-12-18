using System.Collections.Generic;
using System.Linq;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public static class PollExtensions
    {
        public static List<Poll> MemberPolls(this List<Poll> polls)
        {
            return polls.Where(p => p.VotingData.Key == VoteKey.AddFederationMember || p.VotingData.Key == VoteKey.KickFederationMember).ToList();
        }

        public static List<Poll> WhitelistPolls(this List<Poll> polls)
        {
            return polls.Where(p => p.VotingData.Key == VoteKey.RemoveHash || p.VotingData.Key == VoteKey.WhitelistHash).ToList();
        }
    }
}