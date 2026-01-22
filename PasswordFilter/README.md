mkdir "C:\ProgramData\eBZ Tecnologia"
icacls "C:\ProgramData\eBZ Tecnologia" /inheritance:r
icacls "C:\ProgramData\eBZ Tecnologia" /grant:r "SYSTEM:(OI)(CI)(F)"


;;;Native tool command prompt;;;

cl /nologo /W4 /O2 /guard:cf /GS /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /LD passfilter.c /link /DEF:passfilter.def /OUT:eBZpassFilter.dll

cl /LD /MT pwfilter.c /link /out:PasswordFilter.dll

Edit the registry: HKLM\SYSTEM\CurrentControlSet\Control\Lsa => Notification Packages