using System.Collections.Generic;
using Bandwidth.Net.Model;

namespace WebSmsChat
{
  public static class MessageExtensions
  {
    public static Dictionary<string, object> ToDictionary(this Message message)
    {
      return new Dictionary<string, object>
      {
        {"time", message.Time},
        {"direction", message.Direction},
        {"deliveryState", message.DeliveryState},
        {"deliveryCode", message.DeliveryCode},
        {"text", message.Text},
        {"to", message.To},
        {"from", message.From},
        {"state", message.State},
        {"id", message.Id}
      };
    }
  }
}
