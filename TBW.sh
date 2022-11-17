#!/bin/bash
echo "/dev/sda"
echo "--------"
if sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "Samsung SSD 850 EVO 1TB"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "/dev/sda TBW: "($10 * 512) * 1.0e-12, "TB" }'
elif sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "KINGSTON SKC400S371T"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "/dev/sda TBW: "$10 * 1.0e-3, "TB" }'
fi

echo
echo "/dev/sdb"
echo "--------"

if sudo smartctl -a /dev/sdb | grep "^Device Model:" | grep "HITACHI HTS543232L9SA00"; then
	sudo smartctl -A /dev/sdb | grep "^241"
	sudo smartctl -A /dev/sdb | awk '/^241/ { print "/dev/sdb TBW: "$10 * 1.0e-3, "TB" }'
fi

echo
echo "/dev/sdc"
echo "--------"

if sudo smartctl -a /dev/sdc | grep "^Device Model:" | grep "KINGSTON SA400S37480G"; then
	sudo smartctl -A /dev/sdc | grep "^241"
	sudo smartctl -A /dev/sdc | awk '/^241/ { print "/dev/sdc TBW: "$10 * 1.0e-3, "TB" }'
fi

echo
echo "/dev/nvme0n1"
echo "------------"

if sudo smartctl -a /dev/nvme0n1 | grep "^Model Number:" | grep "Samsung SSD 970 EVO Plus"; then
	#sudo smartctl -a /dev/nvme0n1 | grep "^Model Number:"
	sudo smartctl -a /dev/nvme0n1 | grep "Data Units Written"
fi

