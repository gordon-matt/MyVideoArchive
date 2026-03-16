namespace MyVideoArchive.Models.Requests.Channel;

public record UpdateChannelUserSubscriptionsRequest(IEnumerable<string> SubscribedUserIds);
