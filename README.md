# Kontomanager Client .NET
.NET Client library that wraps the functionalities provided by the kontomanager.at web management interface used by a number of mobile carriers in Austria (MVNOs in the A1 network).

# UPDATE 02/2023
The Kontomanager Web interface has had a major design overhaul. Unfortunately, the WebSMS functionality was removed in the process.
v1.x is no longer working for at least XOXO and YESSS as of 15.02.2023.

v2.0.0 provides basic account usage reading functionality comparable to what was present before. Reading of current monthly cost is not implemented yet.

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

# Usage

### Basic Example
```c#
var client = new XOXOClient("<login_username/number>", "<login_password>")
    .EnableDebugLogging(true) // Enables Console Log outputs
    .UseAutoReconnect(true); // Enables automatic re-login after a connection timeout
    
await client.CreateConnection();

var usage = await client.GetAccountUsage();
usage.PrintToConsole();
```

### 1.2.0 Additions

```c#
var client = new XOXOClient("<login_username/number>", "<login_password>")
    .EnableDebugLogging(true) // Enables Console Log outputs
    .UseAutoReconnect(true); // Enables automatic re-login after a connection timeout
    
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

# Similar projects

The following projects seem to do the same thing as this client in other languages. However, I did not test any of them.

- Node.JS client [https://github.com/mklan/educom-sms](https://github.com/mklan/educom-sms)
- Python Client [https://github.com/cynicer/educom-web-sms](https://github.com/cynicer/educom-web-sms)
- Python Client [https://git.flo.cx/flowolf/yessssms](https://git.flo.cx/flowolf/yessssms)

# Changelog

### 20.02.2023 2.0.2
- bug fixes
- 
### 16.02.2023 2.0.1
- bug fixes

### 15.02.2023 2.0.0
This is a breaking change. Some methods were removed and the constructor was refactored to only require one URL.
- remove no longer supported WebSMS functionality
- add support for new Kontomanager UI

### 07.06.2022 1.2.7
- added a function to extract the selected phone number from the header

### 07.06.2022 1.2.6
- fix issue that caused an exception when the settings.php page redirects to kundendaten.php for disabled phone numbers

### 03.06.2022 1.2.5
- fix issue that caused an exception when trying to switch to a phone number that has been deactivated

### 20.05.2022 1.2.3
- fix issue with package validity not being read for some packages
- add .net6 as target framework

### 19.05.2022 1.2.2
- fix wrong used data number for eu data

### 19.05.2022: 1.2.1
- fix bad return value in CreateConnection

### 19.05.2022: 1.2.0

- add support for multiple sims managed under one account
- add support for reading basic information on the contract (available min/sms/mb, ...)
