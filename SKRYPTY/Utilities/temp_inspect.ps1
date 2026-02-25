$assemblyPath = 'd:/Development/SUSModder/SUSModder/bin/Debug/net10.0-windows/Velopack.dll'

try {
	$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
} catch {
	Write-Error "Failed to load assembly: $($_.Exception.Message)"
	if ($_.Exception -and $_.Exception.InnerException) {
		Write-Error "Inner: $($_.Exception.InnerException.Message)"
	}
	return
}

try {
	$types = $assembly.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
	Write-Error "ReflectionTypeLoadException: $($_.Exception.Message)"
	foreach ($ex in $_.Exception.LoaderExceptions) {
		Write-Error "LoaderException: $($ex.Message)"
	}
	return
}

$matched = $types | Where-Object { $_.FullName -like '*SimpleWebSource*' }
if (-not $matched) {
	Write-Error 'Failed to find any type containing SimpleWebSource'
	$types | Sort-Object FullName | Select-Object -First 50 FullName | Format-Table -AutoSize
	return
}

foreach ($t in $matched) {
	Write-Host "Type:" $t.FullName
	$flags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic
	$t.GetMethods($flags) | Select-Object Name, IsPublic, IsVirtual, DeclaringType | Format-Table -AutoSize
}
[void][System.Reflection.Assembly]::LoadFrom('d:/Development/SUSModder/SUSModder/bin/Debug/net10.0-windows/Velopack.dll')
$type = [Velopack.Sources.SimpleWebSource]
$flags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic
$methods = $type.GetMethods($flags)
$methods | Select-Object Name, IsPublic, IsVirtual, DeclaringType | Format-Table -AutoSize
