# radio-updater

Usage : radio-uploader.exe [-f firmware_file] -port com_port [-b baud_rate] [-c config_file]
* -f  - path to radio firware file
* -port  - com port to connect
* -b  - baud rate at com port, if not set default 57600 is used
* -c  - path to configuration file
* -h  - display this help

Example: radio-uploader.exe -port COM25 -b 57600 -c gps_rtk.cfg
