# cTrader bots & indicators
Can be easily connected to your cTrader terminal.
1. Build the project you need.
2. Get the indicator or bot `/bin/Release/net6.0/<bot_project_name>.algo`.
3. Place it to `/cAlgo/Sources/Indicators` or `/cAlgo/Sources/Robots`.
4. Find it in cTrader and connect to a chart/backtesting/real trading
5. ???
6. PROFIT!

## Price Action patterns
- indicator `PriceActionIndicator.csproj`

    [ ![Indicator in work](images/priceActionInd_preview.png) ](images/priceActionInd.png)

## Gartley patterns + MACD divergences (bot & indicator)
- indicator `GartleyFinderIndicator.csproj`

    [ ![Indicator in work](images/gartleyInd_preview.png) ](images/gartleyInd.png)

- bot `GartleySignalerBot.csproj`

## Elliott Wave Impulse Trading System (bot & indicator)
Setup principle is to find an initial impulse (wave 1 or wave A) and trade the next wave from the pullback.

![Setup](images/ew_setup.png)

- indicator `ImpulseFinderIndicator.csproj`

    [ ![Indicator in work](images/impulseFinderInd_preview.png) ](images/impulseFinderInd.png)

- bot `ImpulseSignalerBot.csproj`

    [ ![Bot in work](images/impulseFinderBot_preview.png) ](images/impulseFinderBot.png)

## Signals checker bot & indicator
- indicator `SignalsCheckIndicator.csproj`

    [ ![Indicator in work](images/signalsCheckerInd_preview.png) ](images/signalsCheckerInd.png)

- bot `SignalCheckerBot.csproj`

    [ ![Bot in work](images/signalsCheckerBot_preview.png) ](images/signalsCheckerBot.png)

## Rate-based bot
- bot `RateSignalerBot.csproj`

    [ ![Bot in work](images/rateSignalerBot_preview.png) ](images/rateSignalerBot.png)

## Telegram reporting

![Bot in work](images/telegramSignalsBot.png)

