# Change Log
All notable changes to this project will be documented in this file.


## [2.0.6] - 2023-03-27
- fix bug that parsed the remaining EU data incorrectly

## [2.0.5] - 2023-03-07
- fix bug that returned null instead of an empty list for selectable phone numbers

## [2.0.4] - 2023-02-28
- fix bug that caused prepaid credit to be read incorrectly if system culture was not german

## [2.0.3] - 2023-02-20
- includes fix intended for 2.0.2

## [2.0.2] - 2023-02-20
- bug fixes

## [2.0.1] - 2023-02-16
- bug fixes

## [2.0.0] - 2023-02-15
This is a breaking change. Some methods were removed and the constructor was refactored to only require one URL.
- remove no longer supported WebSMS functionality
- add support for new Kontomanager UI

## [1.2.7] - 2022-06-07
- added a function to extract the selected phone number from the header

## [1.2.6] - 2022-06-07
- fix issue that caused an exception when the settings.php page redirects to kundendaten.php for disabled phone numbers

## [1.2.5] - 2022-06-03
- fix issue that caused an exception when trying to switch to a phone number that has been deactivated

## [1.2.3] - 2022-05-20
- fix issue with package validity not being read for some packages
- add .net6 as target framework

## [1.2.2] - 2022-05-19
- fix wrong used data number for eu data

## [1.2.1] - 2022-05-19
- fix bad return value in CreateConnection

## [1.2.0] - 2022-05-19

- add support for multiple sims managed under one account
- add support for reading basic information on the contract (available min/sms/mb, ...)
null- 
## [1.1.0] - 2022-01-03

Renamed EducomClient to XOXOClient following the rebranding of the carrier.
Note: Currently, there seems to be an automatic redirect from educom.kontomanager.at to xoxo.kontomanager.at,
so EducomClient from previous versions should continue to work.
EducomClient was renamed because the Educom brand continues to exist, but does not use Kontomanager anymore.

### Added

### Changed

- RENAME EducomClient to XOXOClient
- CHANGE Kontomanager URLs used for Educom/XOXO

### Fixed

## [1.0.0] - Initial Release