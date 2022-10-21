using System;
using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Enums;

/// <summary>
/// This enum is incredibly important to Rumble security.  When used in token generation, the Display attribute
/// is used to fill in the audience for the token.  The audience determines which servers a token has access to.
/// 
/// Whenever any new project begins, to be compatible with Rumble token security:
///     1. Add an entry to this enum.
///     2. Bump the common version numbers and push changes.
///     3. Update token-service's common package and push changes.
/// 
/// If the new project is something that players should be able to access, you will also need to update a constant in:
///     player-service > Controllers > TopController > TOKEN_AUDIENCE
/// </summary>
[Flags]
public enum Audience
{
    [Display(Name = "*")]                       All                     = 0b1111_1111_1111_1111,
    [Display(Name = "calendar-service")]        CalendarService         = 0b0000_0000_0000_0001,
    [Display(Name = "chat-service")]            ChatService             = 0b0000_0000_0000_0010,
    [Display(Name = "dmz-service")]             DmzService              = 0b0000_0000_0000_0100,
    [Display(Name = "config-service")]          DynamicConfigService    = 0b0000_0000_0000_1000,
    [Display(Name = "interview-service")]       InterviewService        = 0b0000_0000_0001_0000,
    [Display(Name = "leaderboard-service")]     LeaderboardService      = 0b0000_0000_0010_0000,
    [Display(Name = "mail-service")]            MailService             = 0b0000_0000_0100_0000,
    [Display(Name = "marketplace")]             Marketplace             = 0b0000_0000_1000_0000,
    [Display(Name = "matchmaking-service")]     MatchmakingService      = 0b0000_0001_0000_0000,
    [Display(Name = "multiplayer-service")]     MultiplayerService      = 0b0000_0010_0000_0000,
    [Display(Name = "nft-service")]             NftService              = 0b0000_0100_0000_0000,
    [Display(Name = "player-service")]          PlayerService           = 0b0000_1000_0000_0000,
    [Display(Name = "portal")]                  Portal                  = 0b0001_0000_0000_0000,
    [Display(Name = "pvp-service")]             PvpService              = 0b0010_0000_0000_0000,
    [Display(Name = "receipt-service")]         ReceiptService          = 0b0100_0000_0000_0000,
    [Display(Name = "token-service")]           TokenService            = 0b1000_0000_0000_0000
}