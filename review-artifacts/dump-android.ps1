param([string]$Name = 'android-ui')
adb -s f54f4285 shell uiautomator dump /sdcard/axis-review.xml 2>$null | Out-Null
adb -s f54f4285 pull /sdcard/axis-review.xml "review-artifacts/$Name.xml" 2>$null | Out-Null
[xml]$ui = Get-Content "review-artifacts/$Name.xml"
$ui.SelectNodes('//node') | Where-Object { $_.text -or $_.'content-desc' -or $_.class -eq 'android.widget.EditText' } | ForEach-Object { '{0} | {1} | {2} | {3}' -f $_.class,$_.text,$_.'content-desc',$_.bounds }
