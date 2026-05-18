[Skip to content](https://github.com/buttplugio/stpihkal/issues/139#start-of-content)

You signed in with another tab or window. [Reload](https://github.com/buttplugio/stpihkal/issues/139) to refresh your session.You signed out in another tab or window. [Reload](https://github.com/buttplugio/stpihkal/issues/139) to refresh your session.You switched accounts on another tab or window. [Reload](https://github.com/buttplugio/stpihkal/issues/139) to refresh your session.Dismiss alert

{{ message }}

[buttplugio](https://github.com/buttplugio)/ **[stpihkal](https://github.com/buttplugio/stpihkal)** Public

- [Notifications](https://github.com/login?return_to=%2Fbuttplugio%2Fstpihkal) You must be signed in to change notification settings
- [Fork\\
21](https://github.com/login?return_to=%2Fbuttplugio%2Fstpihkal)
- [Star\\
95](https://github.com/login?return_to=%2Fbuttplugio%2Fstpihkal)


# Document Hismith Protocol\#139

[New issue](https://github.com/login?return_to=https://github.com/buttplugio/stpihkal/issues/139)

Copy link

[New issue](https://github.com/login?return_to=https://github.com/buttplugio/stpihkal/issues/139)

Copy link

Closed

Closed

[Document Hismith Protocol](https://github.com/buttplugio/stpihkal/issues/139#top)#139

Copy link

[![@blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=80)](https://github.com/blackspherefollower)

## Description

[![@blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=48)](https://github.com/blackspherefollower)

[blackspherefollower](https://github.com/blackspherefollower)

opened [on Sep 6, 2021on Sep 6, 2021](https://github.com/buttplugio/stpihkal/issues/139#issue-989245988)

Last edited by blackspherefollower

Contributor

Issue body actions

BLE Name: HISMITH

Tx Service UUID: 0000ffe5-0000-1000-8000-00805f9b34fb

Tx Characteristic UUID: 0000ffe9-0000-1000-8000-00805f9b34fb

Rx Service UUID: 0000ffe0-0000-1000-8000-00805f9b34fb

Rx Characteristic UUID: 0000ffe4-0000-1000-8000-00805f9b34fb

Info Service UUID: 0000ff90-0000-1000-8000-00805f9b34fb

Model Characteristic UUID: 0000ff96-0000-1000-8000-00805f9b34fb

There seems to be 2 write modes:

- 0xAA, 0xXX, 0xYY 0xZZ (ZZ = XX + YY)
- 0xFF, 0xXX, 0xYY 0xZZ (ZZ = XX + YY)

AA commands:

- Power on: X=1 Y=0
- Power off: X=2 Y=0
- Get Speed: X=3 Y=0
- Set Speed: X=4 Y=(0x00-0x64)
- Get Mode: X=5 Y=0
- Set Mode: X=5 Y=?
- Get Vibrate: X=6 Y=0
- Set Vibrate: X=6 Y=?
- Close Vibrate: X=6 Y=0xf0
- Get Music: X=7 Y=0
- Open Music: X=7 Y=1
- Close Music: X=7 Y=0xf0

FF commands:

- FF Auto Mode: X=1 Y=0
- FF Manual Mode: X=1 Y=1
- Query FF Run Mode: X=1 Y=a0
- FF Stop Off Mode: X=2 Y=0
- Query FF Speed: X=3 Y=a0
- Set FF Speed: X=3 Y=?
- Query FF Mode: X=4 Y=a0
- Set FF Mode: X=4 Y=?
- FF New Mode: 0xFF, 0x05, 0x05, 0xXX, 0x(5+XX), )xYY..., 0xEE (where XX is the length of the hex string YY)
- Query FF Stop Bit: X=6 Y=a0
- Set FF Stop Bit: X=6 Y=?
- Query FF Start Bit: X=7 Y=a0
- Set FF Start Bit: X=7 Y=?
- Query FF Smoothness: X=8 Y=a0
- Set FF Smoothness: X=8 Y=?
- Set FF Position: X=9 Y=?

Product info available via the hismith api:

- `https://www.hismiths.com/api/userLogin?userName=<email>&userPass=<pass>&loginType=app` gets you an auth token
- `https://www.hismiths.com/api/getProducts?token=<token>` gets you:

```
{
  "success": 1,
  "userLangIso": "en",
  "products": [\
    {\
      "code": "1001",\
      "name": "AK Series",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/AKSeries.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "[{\"s\":5,\"e\":55,\"t\":8},{\"s\":55,\"e\":5,\"t\":8}]",\
          "mIcon": "https://file.hismiths.com/apm/1/01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "[{\"s\":66,\"e\":66,\"t\":3.1},{\"s\":0,\"e\":0,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/02_1.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "[{\"s\":48,\"e\":48,\"t\":1.4},{\"s\":68,\"e\":68,\"t\":0.8},{\"s\":0,\"e\":0,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/03_1.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "[{\"s\":30,\"e\":30,\"t\":1},{\"s\":50,\"e\":50,\"t\":1},{\"s\":40,\"e\":40,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":2},{\"s\":50,\"e\":50,\"t\":1.2}]",\
          "mIcon": "https://file.hismiths.com/apm/1/05_1.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":1.2},{\"s\":40,\"e\":40,\"t\":0.6},{\"s\":0,\"e\":0,\"t\":0.6}]",\
          "mIcon": "https://file.hismiths.com/apm/1/06.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "[{\"s\":30,\"e\":30,\"t\":0.3},{\"s\":80,\"e\":80,\"t\":0.2},{\"s\":0,\"e\":0,\"t\":0.2}]",\
          "mIcon": "https://file.hismiths.com/apm/1/07.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":1.2},{\"s\":100,\"e\":100,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/08.png"\
        }\
      ]\
    },\
    {\
      "code": "1002",\
      "name": "Pro Traveler",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/ProTraveler.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/02.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/03.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/06.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/07.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/08.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/09.png"\
        },\
        {\
          "mCommand": "AA05090E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/10.png"\
        }\
      ]\
    },\
    {\
      "code": "1003",\
      "name": "Sex Droid",\
      "climax": "1",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/Sexdroid.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/01_1.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/02_1.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/03_1.png"\
        }\
      ]\
    },\
    {\
      "code": "2001",\
      "name": "AIM Cup",\
      "climax": "0",\
      "music": "1",\
      "logo": "https://file.hismiths.com/product/7AAE-EED7-1829-3592-5A5BB1618357.jpg",\
      "type": "sm2",\
      "vibrations": [\
        {\
          "mCommand": "AA060107",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration01.png"\
        },\
        {\
          "mCommand": "AA060208",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration02_1.png"\
        },\
        {\
          "mCommand": "AA060309",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration03.png"\
        },\
        {\
          "mCommand": "AA06040A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration04.png"\
        },\
        {\
          "mCommand": "AA06050B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration05.png"\
        },\
        {\
          "mCommand": "AA06060C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration06.png"\
        },\
        {\
          "mCommand": "AA06070D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration07.png"\
        },\
        {\
          "mCommand": "AA06080E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration08.png"\
        },\
        {\
          "mCommand": "AA06090F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration09.png"\
        }\
      ],\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode02.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode03.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode05.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode06.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode07.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode08.png"\
        },\
        {\
          "mCommand": "AA05090E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode09.png"\
        },\
        {\
          "mCommand": "AA050A0F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode10.png"\
        }\
      ]\
    }\
  ]
}
```

The "Capsule/Sexdroid" model 0x1003 responds to:

- Set Speed (0xAA 04 00-64 XX)
- Set Mode (0xAA 05 01-03 XX) - aliases for speed settings, must use 0xAA040004 to stop
- "Climax" (0xAA 06 01 07) - puts the device in overdrive for a few seconds then stops, speed value seems to be ignored

The "Kya Thrusting Masturbation Cup/AIM Cup" model 0x2001 responds to:

- Set Speed (0xAA 04 00-64 XX)
- Set Mode (0xAA 05 01-0A XX) - speed patterns, must use 0xAA040004 to stop
- Vibrate (0xAA 06 01-09 XX) - vibration patterns, only 01 is constant, must use 0xAA06f0f4 to stop
- Audio (0xAA 07 01 08) - turns on audio output to the headphone socket, must use 0xAA07f0f7 to stop

## Activity

[![blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=80)](https://github.com/blackspherefollower)

### blackspherefollower commented on Nov 4, 2022on Nov 4, 2022

[![@blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=48)](https://github.com/blackspherefollower)

[blackspherefollower](https://github.com/blackspherefollower)

[on Nov 4, 2022on Nov 4, 2022](https://github.com/buttplugio/stpihkal/issues/139#issuecomment-1303931925)

ContributorAuthor

More actions

The Wildolo devices (a subsidiary of Hismith) have released a set of large vibrating dildos that use a very similar protocol:

BLE Name: Wildolo

Model: 0x3001

- Set Vibration Speed (0xAA 04 00-64 XX)
- Set Vibration Pattern (0xAA 05 01-0a XX)

[![blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=80)](https://github.com/blackspherefollower)

### blackspherefollower commented on Nov 7, 2022on Nov 7, 2022

[![@blackspherefollower](https://avatars.githubusercontent.com/u/29165182?u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4&size=48)](https://github.com/blackspherefollower)

[blackspherefollower](https://github.com/blackspherefollower)

[on Nov 7, 2022on Nov 7, 2022](https://github.com/buttplugio/stpihkal/issues/139#issuecomment-1305364825)

ContributorAuthor

More actions

The updated products list:

```
{
  "success": 1,
  "products": [\
    {\
      "code": "1004",\
      "name": "Hismith Mini",\
      "shake": "50",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/HismithMini.jpg",\
      "type": "CC_SM"\
    },\
    {\
      "code": "1001",\
      "name": "AK Series",\
      "shake": "50",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/AKSeries.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "[{\"s\":5,\"e\":55,\"t\":8},{\"s\":55,\"e\":5,\"t\":8}]",\
          "mIcon": "https://file.hismiths.com/apm/1/01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "[{\"s\":66,\"e\":66,\"t\":3.1},{\"s\":0,\"e\":0,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/02_1.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "[{\"s\":48,\"e\":48,\"t\":1.4},{\"s\":68,\"e\":68,\"t\":0.8},{\"s\":0,\"e\":0,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/03_1.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "[{\"s\":30,\"e\":30,\"t\":1},{\"s\":50,\"e\":50,\"t\":1},{\"s\":40,\"e\":40,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":2},{\"s\":50,\"e\":50,\"t\":1.2}]",\
          "mIcon": "https://file.hismiths.com/apm/1/05_1.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":1.2},{\"s\":40,\"e\":40,\"t\":0.6},{\"s\":0,\"e\":0,\"t\":0.6}]",\
          "mIcon": "https://file.hismiths.com/apm/1/06.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "[{\"s\":30,\"e\":30,\"t\":0.3},{\"s\":80,\"e\":80,\"t\":0.2},{\"s\":0,\"e\":0,\"t\":0.2}]",\
          "mIcon": "https://file.hismiths.com/apm/1/07.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "[{\"s\":80,\"e\":80,\"t\":1.2},{\"s\":100,\"e\":100,\"t\":0.5}]",\
          "mIcon": "https://file.hismiths.com/apm/1/08.png"\
        }\
      ]\
    },\
    {\
      "code": "1002",\
      "name": "Pro Traveler",\
      "shake": "0",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/ProTraveler.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/02.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/03.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/06.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/07.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/08.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/09.png"\
        },\
        {\
          "mCommand": "AA05090E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/2/10.png"\
        }\
      ]\
    },\
    {\
      "code": "1003",\
      "name": "Sex Droid",\
      "shake": "0",\
      "slide": "20",\
      "climax": "1",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/Sexdroid.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/01_1.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/02_1.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/4/03_1.png"\
        }\
      ]\
    },\
    {\
      "code": "1005",\
      "name": "Hismith S1",\
      "shake": "50",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/1005.jpg",\
      "type": "CC_SM",\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/01.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/02.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/03.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/04.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/05.png"\
        },\
        {\
          "mCommand": "CC04060a",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/06.png"\
        },\
        {\
          "mCommand": "CC04070b",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/07.png"\
        },\
        {\
          "mCommand": "CC04080c",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/10/08.png"\
        }\
      ]\
    },\
    {\
      "code": "1101",\
      "name": "Hismith S2",\
      "shake": "0",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/H128X128.png",\
      "type": "CC_SMS",\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M01.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M02.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M03.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M04.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M05.png"\
        },\
        {\
          "mCommand": "CC04060A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M06.png"\
        },\
        {\
          "mCommand": "CC04070B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M07.png"\
        },\
        {\
          "mCommand": "CC04080C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/7/M08.png"\
        }\
      ]\
    },\
    {\
      "code": "3001",\
      "name": "Wildolo",\
      "shake": "100",\
      "slide": "100",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/icon.png",\
      "type": "sm",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode02.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode03.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode05.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode06.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode07.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode08.png"\
        },\
        {\
          "mCommand": "AA05090E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode09.png"\
        },\
        {\
          "mCommand": "AA050A0F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/6/mode10.png"\
        }\
      ]\
    },\
    {\
      "code": "4001",\
      "name": "Auxfun Box",\
      "shake": "50",\
      "slide": "20",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/HismithMini.jpg",\
      "type": "CC_SM",\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/01.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/02.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/03.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/041.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/051.png"\
        },\
        {\
          "mCommand": "CC04060A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/061.png"\
        },\
        {\
          "mCommand": "CC04070B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/071.png"\
        },\
        {\
          "mCommand": "CC04080C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/9/08.png"\
        }\
      ]\
    },\
    {\
      "code": "2001",\
      "name": "AIM Cup",\
      "shake": "100",\
      "slide": "100",\
      "climax": "0",\
      "music": "1",\
      "logo": "https://file.hismiths.com/product/7AAE-EED7-1829-3592-5A5BB1618357.jpg",\
      "type": "sm2",\
      "modes": [\
        {\
          "mCommand": "AA050106",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode01.png"\
        },\
        {\
          "mCommand": "AA050207",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode02.png"\
        },\
        {\
          "mCommand": "AA050308",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode03.png"\
        },\
        {\
          "mCommand": "AA050409",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode04.png"\
        },\
        {\
          "mCommand": "AA05050A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode05.png"\
        },\
        {\
          "mCommand": "AA05060B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode06.png"\
        },\
        {\
          "mCommand": "AA05070C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode07.png"\
        },\
        {\
          "mCommand": "AA05080D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode08.png"\
        },\
        {\
          "mCommand": "AA05090E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode09.png"\
        },\
        {\
          "mCommand": "AA050A0F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/mode10.png"\
        }\
      ],\
      "vibrations": [\
        {\
          "mCommand": "AA060107",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration01.png"\
        },\
        {\
          "mCommand": "AA060208",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration02_1.png"\
        },\
        {\
          "mCommand": "AA060309",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration03.png"\
        },\
        {\
          "mCommand": "AA06040A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration04.png"\
        },\
        {\
          "mCommand": "AA06050B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration05.png"\
        },\
        {\
          "mCommand": "AA06060C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration06.png"\
        },\
        {\
          "mCommand": "AA06070D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration07.png"\
        },\
        {\
          "mCommand": "AA06080E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration08.png"\
        },\
        {\
          "mCommand": "AA06090F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/5/vibration09.png"\
        }\
      ]\
    },\
    {\
      "code": "2101",\
      "name": "Eropair Cup",\
      "shake": "100",\
      "slide": "100",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/A.jpg",\
      "type": "CC_SMV",\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M1.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M2.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M3.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M4.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M5.png"\
        },\
        {\
          "mCommand": "CC04060A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M6.png"\
        },\
        {\
          "mCommand": "CC04070B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M7.png"\
        },\
        {\
          "mCommand": "CC04080C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M8.png"\
        },\
        {\
          "mCommand": "CC04090D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M9.png"\
        },\
        {\
          "mCommand": "CC040A0E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/M10.png"\
        }\
      ],\
      "vibrations": [\
        {\
          "mCommand": "CC060107",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V01.png"\
        },\
        {\
          "mCommand": "CC060208",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V02.png"\
        },\
        {\
          "mCommand": "CC060309",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V03.png"\
        },\
        {\
          "mCommand": "CC06040A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V4.png"\
        },\
        {\
          "mCommand": "CC06050B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V5.png"\
        },\
        {\
          "mCommand": "CC06060C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V6.png"\
        },\
        {\
          "mCommand": "CC06070D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V7.png"\
        },\
        {\
          "mCommand": "CC06080E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V8.png"\
        },\
        {\
          "mCommand": "CC06090F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V9.png"\
        },\
        {\
          "mCommand": "CC060A10",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/12/V10.png"\
        }\
      ]\
    },\
    {\
      "code": "2201",\
      "name": "Sinloli",\
      "shake": "100",\
      "slide": "100",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/butt.jpg",\
      "type": "CC_SMV",\
      "vibrations": [\
        {\
          "mCommand": "CC060107",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V01.png"\
        },\
        {\
          "mCommand": "CC060208",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V02.png"\
        },\
        {\
          "mCommand": "CC060309",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V03.png"\
        },\
        {\
          "mCommand": "CC06040A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V04.png"\
        },\
        {\
          "mCommand": "CC06050B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V05.png"\
        },\
        {\
          "mCommand": "CC06060C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V06.png"\
        },\
        {\
          "mCommand": "CC06070D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V07.png"\
        },\
        {\
          "mCommand": "CC06080E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/V08.png"\
        }\
      ],\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S01.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S02.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S03.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S04.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S05.png"\
        },\
        {\
          "mCommand": "CC04060A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S06.png"\
        },\
        {\
          "mCommand": "CC04070B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S07.png"\
        },\
        {\
          "mCommand": "CC04080C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/8/S08.png"\
        }\
      ]\
    },\
    {\
      "code": "3101",\
      "name": "Eropair V1",\
      "shake": "100",\
      "slide": "100",\
      "climax": "0",\
      "music": "0",\
      "logo": "https://file.hismiths.com/product/V1.png",\
      "type": "CC_SMV",\
      "vibrations": [\
        {\
          "mCommand": "CC060107",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V01.png"\
        },\
        {\
          "mCommand": "CC060208",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V02.png"\
        },\
        {\
          "mCommand": "CC060309",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V03.png"\
        },\
        {\
          "mCommand": "CC06040A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V4.png"\
        },\
        {\
          "mCommand": "CC06050B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V5.png"\
        },\
        {\
          "mCommand": "CC06060C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V6.png"\
        },\
        {\
          "mCommand": "CC06070D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V7.png"\
        },\
        {\
          "mCommand": "CC06080E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V8.png"\
        },\
        {\
          "mCommand": "CC06090F",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V9.png"\
        },\
        {\
          "mCommand": "CC060A10",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/V10.png"\
        }\
      ],\
      "modes": [\
        {\
          "mCommand": "CC040105",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M1.png"\
        },\
        {\
          "mCommand": "CC040206",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M2.png"\
        },\
        {\
          "mCommand": "CC040307",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M3.png"\
        },\
        {\
          "mCommand": "CC040408",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M4.png"\
        },\
        {\
          "mCommand": "CC040509",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M5.png"\
        },\
        {\
          "mCommand": "CC04060A",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M6.png"\
        },\
        {\
          "mCommand": "CC04070B",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M7.png"\
        },\
        {\
          "mCommand": "CC04080C",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M8.png"\
        },\
        {\
          "mCommand": "CC04090D",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M9.png"\
        },\
        {\
          "mCommand": "CC040A0E",\
          "mCommands": "",\
          "mIcon": "https://file.hismiths.com/apm/11/M10.png"\
        }\
      ]\
    }\
  ]
}
```

So the devices are now:

| Code | Brand/Model | Type | Type Code |
| --- | --- | --- | --- |
| 0x1001 | AK Series - Full scale Hismith | Oscillating machine | sm |
| 0x1002 | Pro Traveler | thrusting insertable | sm |
| 0x1003 | Sex Droid | thrusting insertable | sm |
| 0x1004 | Hismith Mini/Violin | Oscillating machine | CC\_SM |
| 0x1005 | Hismith S1 | Oscillating machine? | CC\_SM |
| 0x1101 | Hismith S2 | Oscillating machine? | CC\_SMS |
| 0x2001 | AIM Cup | Stroker/Vibrator | sm2 |
| 0x2101 | Eropair Cup | Vibrator? | CC\_SMV |
| 0x2201 | Sinloli | Vibrator? | CC\_SMV |
| 0x3001 | Wildolo | Vibrator | sm |
| 0x3101 | Eropair V1 | Rabbit? | CC\_SMV |
| 0x4001 | Auxfun Box | Oscillating machine? | CC\_SM |

[![](https://avatars.githubusercontent.com/u/34539?s=64&u=72489a6f0d45c9c0633968be820be0b9df9918eb&v=4)qdot](https://github.com/qdot)

closed this as [completed](https://github.com/buttplugio/stpihkal/issues?q=is%3Aissue%20state%3Aclosed%20archived%3Afalse%20reason%3Acompleted) in [b443275](https://github.com/buttplugio/docs.buttplug.io/commit/b443275e7fe9e86584a918991cb5c44f405b372a) [on Feb 28on Feb 28, 2026](https://github.com/buttplugio/stpihkal/issues/139#event-23165121434)

[Sign up for free](https://github.com/signup?return_to=https://github.com/buttplugio/stpihkal/issues/139)**to join this conversation on GitHub.** Already have an account? [Sign in to comment](https://github.com/login?return_to=https://github.com/buttplugio/stpihkal/issues/139)

## Metadata

## Metadata

### Assignees

No one assigned

### Labels

No labels

No labels

### Type

No type

### Projects

No projects

### Milestone

No milestone

### Relationships

None yet

### Development

No branches or pull requests

### Participants

[![@blackspherefollower](https://avatars.githubusercontent.com/u/29165182?s=64&u=7da637d7bfe375c31d358005e0fa1fb2cbfa5830&v=4)](https://github.com/blackspherefollower)

## Issue actions

You can’t perform that action at this time.