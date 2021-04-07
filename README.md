# Kontomanager Websms Client .NET
.NET Client library that wraps the WebSMS functionality provided by the kontomanager.at web management interface used by a number of mobile carriers in Austria.

# Installation

Simply install the nuget package from [https://www.nuget.org/packages/KontomanagerClient](https://www.nuget.org/packages/KontomanagerClient) to your project.

# Carrier Support
The library was testet for:
- [Educom](https://www.educom.at) @ [educom.kontomanager.at](educom.kontomanager.at)
- [Yesss](https://www.yesss.at) @ [https://www.yesss.at/kontomanager.at/](https://www.yesss.at/kontomanager.at/)

Other carriers that use Kontomanager but were not tested include:
- [Georg](https://georg.at) @ [https://kundencenter.georg.at](https://kundencenter.georg.at)
- [BilliTel](https://billitel.at) @ [https://billitel.kontomanager.at](https://billitel.kontomanager.at)
- [Goood](https://goood-mobile.at/) @ [https://goood.kontomanager.at](https://goood.kontomanager.at)
- [Simfonie](https://www.simfonie.at/home) @ [https://simfonie.kontomanager.at](https://simfonie.kontomanager.at)

And possibly more. Feel free to add carriers to that list.

# Limitations
The tested carriers each limit the maximum number of messages that may be sent per hour and phone number to **50**.
Unfortunatly, it is not possible to read the remaining time until a new message can be sent, so best the client can do is guess the time, unless all messages were sent from the currently running instance of THIS client.

# Usage

Basic Example
```c#
var client = new EducomClient("<login_username/number>", "<login_password>")
    .EnableDebugLogging(true) // Enables Console Log outputs
    .UseAutoReconnect(true) // Enables automatic re-login after a connection timeout
    .UseQueue() // Enables a queue that reattempts to send messages when the SendLimit is reached
    .ThrowExceptionOnInvalidNumberFormat(true); // Configures the client to throw an exception if a phone number format was rejected by Kontomanager
    
var r = await client.SendMessage("<recipient_number>", "<message>");
```

When UseQueue() is called, the response from SendMessage is always **MessageEnqueued**.
To get the actual sending results, the **SendingAttempted** event of the Message class can be used like this.

```c#
Message m = new Message("<recipient_number>", "<message>");
m.SendingAttempted += (sender, result) =>
{
    // result is the sending result enum
    // keep in mind that this event can be called multiple times in case Sending fails
    // MessageSendResult.Ok is only returned once the message has been successfully sent.
};
var r = await client.SendMessage(m);
```

### Supported Phone Number Formats

Kontomanager requires phone numbers to either specify an austrian carrier specific number prefix, or specify a number including a country prefix starting in 00.
This client uses the latter case exclusively. Numbers can either be specified as **00<country_code><number>** or **+<country_code><number>**.

Valid examples are (in this example: +43 = Austrian Country Code, 0664: Provider Prefix for A1 (0 is omitted if country prefix is used):
- +436641234567
- 00436641234567

Invalid:
- 436641234567
- 06641234567

