# Kontomanager Websms Client .NET
.NET Client library that wraps the WebSMS functionality provided by the kontomanager.at web management interface used by a number of mobile carriers in Austria (MVNOs in the A1 network).

# Installation

Simply install the nuget package from [https://www.nuget.org/packages/KontomanagerClient](https://www.nuget.org/packages/KontomanagerClient) to your project.

# Carrier Support
The library was testet for:
- [XOXO](https://www.xoxo-mobile.at) @ [xoxo.kontomanager.at](https://xoxo.kontomanager.at)
- [Yesss](https://www.yesss.at) @ [www.yesss.at/kontomanager.at/](https://www.yesss.at/kontomanager.at/)
- [~~Educom~~](https://www.educom.at) @ [~~educom.kontomanager.at~~](https://educom.kontomanager.at) | Educom was rebranded to XOXO

Other carriers that use Kontomanager but were not tested include:
- [Georg](https://georg.at) @ [kundencenter.georg.at](https://kundencenter.georg.at)
- [BilliTel](https://billitel.at) @ [billitel.kontomanager.at](https://billitel.kontomanager.at)
- [Goood](https://goood-mobile.at/) @ [goood.kontomanager.at](https://goood.kontomanager.at)
- [Simfonie](https://www.simfonie.at/home) @ [simfonie.kontomanager.at](https://simfonie.kontomanager.at)

And possibly more. Feel free to add carriers to that list.

# Limitations
The tested carriers each limit the maximum number of messages that may be sent per hour and phone number to **50**.
Unfortunatly, it is not possible to read the remaining time until a new message can be sent, so best the client can do is guess the time, unless all messages were sent from the currently running instance of THIS client.

# Usage

### Basic Example
```c#
var client = new XOXOClient("<login_username/number>", "<login_password>")
    .EnableDebugLogging(true) // Enables Console Log outputs
    .UseAutoReconnect(true) // Enables automatic re-login after a connection timeout
    .UseQueue() // Enables a queue that reattempts to send messages when the SendLimit is reached
    .ThrowExceptionOnInvalidNumberFormat(true); // Configures the client to throw an exception if a phone number format was rejected by Kontomanager
    
await client.CreateConnection();

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

### 1.2.0 Additions

```c#
var client = new XOXOClient("<login_username/number>", "<login_password>")
    .EnableDebugLogging(true) // Enables Console Log outputs
    .UseAutoReconnect(true) // Enables automatic re-login after a connection timeout
    .UseQueue() // Enables a queue that reattempts to send messages when the SendLimit is reached
    .ThrowExceptionOnInvalidNumberFormat(true); // Configures the client to throw an exception if a phone number format was rejected by Kontomanager
    
await client.CreateConnection();

string firstNumber = await client.GetSelectedPhoneNumber(); // returns the currently selected phone number in format 43681...
var usage = await client.GetAccountUsage(); // Get Account usage for firstNumber
usage.PrintToConsole(); // Prints a summary to the console


var numbers = await client.GetSelectablePhoneNumbers(); // gets a list of PhoneNumber object for each number linked to the account (has string number and string subscriberId)
PhoneNumber otherNumber = numbers.First(n => !n.Selected); 
await client.SelectPhoneNumber(otherNumber); // select other phone number for the client
var otherNumberUsage = await client.GetAccountUsage(); // get account usage for otherNumber
otherNumberUsage.PrintToConsole();
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

# Similar projects

The following projects seem to do the same thing as this client in other languages. However, I did not test any of them.

- Node.JS client [https://github.com/mklan/educom-sms](https://github.com/mklan/educom-sms)
- Python Client [https://github.com/cynicer/educom-web-sms](https://github.com/cynicer/educom-web-sms)
- Python Client [https://git.flo.cx/flowolf/yessssms](https://git.flo.cx/flowolf/yessssms)

# Changelog

### 19.05.2022 1.2.2
- fix wrong used data number for eu data

### 19.05.2022: 1.2.1
- fix bad return value in CreateConnection

### 19.05.2022: 1.2.0

- add support for multiple sims managed under one account
- add support for reading basic information on the contract (available min/sms/mb, ...)
