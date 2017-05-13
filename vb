#!/bin/bash
# Copyright Peter Csaszar (Császár Péter) <csjpeter@gmail.com>

GUEST_USER=tester
GUEST_USER_PWD=pwd
VM_NAME_PREFIX=vm
VM_TEMPLATE=
VM_DIR="${HOME}/VirtualBox/"
VBOXNET=vboxnet2
HOST_SSH_PORT_BASE=65300
VM_RAM_SIZE=1024

if [ ! -f vbox-pool/vm.rc ]; then
	echo You must have a vm.rc and some other script file in ./vbox-pool
	echo directory for initializing your virtual machines.
	echo Examples can be found in the vbox subdirectory.
	echo
	echo The first run this script will download the vbox vagrant images into
	echo ~/.vboxes directory and will extract them.
	echo 
    exit 1
fi

source vbox-pool/vm.rc

# To download image files from https://atlas.hashicorp.com/debian/ check http://stackoverflow.com/questions/28399324/download-vagrant-box-file-locally-from-atlas-and-configuring-it

TRAP_LOG="/dev/shm/$(basename $0).trap.log"
set -o pipefail
shopt -s expand_aliases
#trap 'rm \$TRAP_LOG 2> /dev/null || true' EXIT
function trap_handler()
{
    echo $1 > $TRAP_LOG;
    RET=0
    let I=0 || true
    while [ $RET = 0 ]; do
        TRACE=$(caller $I)
        RET=$?
        echo "#$I $TRACE" >> $TRAP_LOG
        let I++ || true
    done;
}
function catch_return ()
{
    ERR_CODE=$?;
    ERR_LINENO=$(cat ${TRAP_LOG} 2>/dev/null | head -n 1);
    ERR_TRACE=$(cat ${TRAP_LOG} 2>/dev/null | tail -n +2);
    return $ERR_CODE;
}
alias try='set +e; ( rm $TRAP_LOG 2> /dev/null; trap "trap_handler \${LINENO}" ERR; set -e;'
alias catch="); catch_return || ";

# param 1 : inflie
# writes to stdout
function generate ()
{
	cat $1 | sed \
		-e "s|@HOST_SSH_PORT@|${HOST_SSH_PORT}|g" \
		-e "s|@VBOX_NETWORK@|${VBOX_NETWORK}|g" \
		-e "s|@GUEST_OS@|${GUEST_OS}|g" \
		-e "s|@GUEST_OS_IMAGE_URL@|${GUEST_OS_IMAGE_URL}|g" \
		-e "s|@GUEST_USER@|${GUEST_USER}|g" \
		-e "s|@GUEST_USER_PWD@|${GUEST_USER_PWD}|g" \
		-e "s|@VM_IP@|${VM_IP}|g" \
		-e "s|@VM_NAME@|${VM_NAME}|g" \
		|| exit $?
}

function vm_name () # ID
{
	local ID=$1
	#ls -1d ${VM_NAME_PREFIX}-*$(printf %03d ${ID}) 2> /dev/null | head -n 1
	echo ${VM_NAME_PREFIX}-$(printf %03d ${ID})
}

function vm_info ()
{
	VM_NAME=$(vm_name ${1})
	source vbox-pool/${VM_NAME}.vbox 2> /dev/null

	INFO=$VM_NAME
	if [ x$GUEST_OS != "x" ]; then
		INFO=$INFO"\t"$GUEST_OS
	fi
	if [ x$VM_TEMPLATE != "x" ]; then
		INFO=$INFO"\t"$VM_TEMPLATE
	fi
	echo -e $INFO
}

function wait_for_workers ()
{
	echo Waiting for all VM to get ready ...
	FAILED_LIST=""

	FAILED=0
	for ID in ${!PIDS[@]}; do
		VM_NAME=$(vm_name ${ID})
		echo Wait for ${VM_NAME} with pid ${PIDS[$ID]}
		wait ${PIDS[$ID]}
		if [ $? != 0 ]; then
			let FAILED++
			echo Worker failed for ${VM_NAME}
			FAILED_LIST=${FAILED_LIST}"\n"$ID
		fi
	done;
	if [ ${FAILED} = 0 ]; then
		echo All worker is successful
		:; # do nothing
	else
		echo ${FAILED}" number of workers failed"
		FAILED_LIST=$(echo -e ${FAILED_LIST} | sort | tr '\n' ' ')
		echo "Failed node IDs: "${FAILED_LIST}
	fi
    return ${FAILED}
}

function precache_images ()
{
	if ! [ -d "/home/${USER}/.vboxes" ]; then
		mkdir ~/.vboxes
	fi

	# Start download processes
	PIDS=()
    ANY_STARTED="no"
	declare -A PIDS
	for I in "${!IMAGES_URL[@]}"; do
		FILE=~/.vboxes/${I}.tar
		if ! [ -e ${FILE} ]; then
			echo Caching ${IMAGES_URL[$I]} into file ${FILE}
			wget -O ${FILE} ${IMAGES_URL[$I]} &
			PIDS["${I}"]=$!
            ANY_STARTED="yes"
		fi
	done

    if [ ${ANY_STARTED} = "yes" ]; then
    	wait_for_workers
    fi

	# extracting tar files
	for I in "${!IMAGES_URL[@]}"; do
		FILE=~/.vboxes/${I}.tar
		f=$(basename ${FILE})
		vmname=${f%.*}
		if ! [ -e ~/.vboxes/${vmname} ]; then
			mkdir ~/.vboxes/${vmname}
			echo Extracting ${FILE} into directory ~/.vboxes/${vmname}
			tar xf ${FILE} -C ~/.vboxes/${vmname}
		fi
	done
}

function vmstatus () # ID
{
	local ID=$1
	local VM_NAME=$(vm_name ${ID})
	STATUSLINE=$(vboxmanage showvminfo ${VM_NAME} | grep "^State:")
	if [[ "$STATUSLINE" =~ ^(.*[ ]+([^ ]+) \(.*)$ ]]; then
		STATUS=${BASH_REMATCH[2]}
		echo $STATUS
	fi
}

function export_config () # ID
{
	local ID=$1
	local VM_NAME=$(vm_name ${ID})

	echo "GUEST_USER=${GUEST_USER}" > vbox-pool/${VM_NAME}.vbox
	echo "GUEST_USER_PWD=${GUEST_USER_PWD}" >> vbox-pool/${VM_NAME}.vbox
	echo "VM_IP=${VBOX_NETWORK}${ID}" >> vbox-pool/${VM_NAME}.vbox
	echo "HOST_SSH_PORT=${HOST_SSH_PORT}" >> vbox-pool/${VM_NAME}.vbox
	echo "VM_NAME=${VM_NAME}" >> vbox-pool/${VM_NAME}.vbox
	echo "VM_TEMPLATE=\"${VM_TEMPLATE}\"" >> vbox-pool/${VM_NAME}.vbox
	echo "GUEST_OS=${GUEST_OS}" >> vbox-pool/${VM_NAME}.vbox
	echo "GUEST_OS_IMAGE_URL=/home/${USER}/.vboxes/${GUEST_OS}.tar" >> vbox-pool/${VM_NAME}.vbox
}

function vm_ssh () # remote command
{
	sshpass -p vagrant /usr/bin/ssh \
		-o StrictHostKeyChecking=no \
		-o UserKnownHostsFile=/dev/null \
		-q -p ${HOST_SSH_PORT} vagrant@127.0.0.1 -- \
		"$@"
		#-o BatchMode \
}

function modifyvm-ram () # ID RAM
{
	local ID=$1
	local RAM=$2
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox
	vboxmanage modifyvm ${VM_NAME} --memory ${RAM}
}

function create () # ID PROVISION
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})

	# VM to be created should not exists yet
	if [ -e vbox-pool/${VM_NAME}.vbox ]; then
		echo Already exists: $(vm_info ${ID})
		exit
	fi

	shift
	# Reading operatig system parameter
	GUEST_OS=""
	if [ "x$1" != "x" ]; then
		for I in "${!IMAGES_URL[@]}"; do
			if [ "x$I" = "x${1}" ]; then
				GUEST_OS=$1
			fi
		done
		if [ "x$GUEST_OS" = "x" ]; then
			echo Unsupported OS: $1
			exit
		fi
	else
		echo Error: Guest OS is not specified
		exit
	fi

	local PROVISION=""
	if [ "x$2" != "x" ]; then
		local PROVISION=$2
	fi

	let HOST_SSH_PORT=${HOST_SSH_PORT_BASE}+${ID}

	export_config ${ID}

	source vbox-pool/${VM_NAME}.vbox

	# creating and setting hostonly network for vbox
	# vboxmanage hostonlyif create
	#vboxmanage hostonlyif ipconfig ${VBOXNET} --ip 192.168.30.1 --netmask 255.255.255.0

	try {
		while read LINE; do
			[[ "${LINE}" =~ ^([^\"]*\"(.*)--disk path.*)$ ]] || true
			DISK_SLOT=${BASH_REMATCH[2]}
			[[ "${DISK_SLOT}" =~ ^(.*unit ([0-9]*).*)$ ]] || true
			UNIT_ID=${BASH_REMATCH[2]}
			DISK_SLOTS="${DISK_SLOTS} ${DISK_SLOT} --disk ${VM_DIR}${VM_NAME}/disk${UNIT_ID}.vmdk"
		done <<< "$(vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf -n 2> /dev/null | grep '\--disk path')"
		[[ "$(vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf -n 2> /dev/null | grep '\--cpus')" =~ ^([^\"]*\"(.*)--cpus.*)$ ]] || true
		CPU_SLOT=${BASH_REMATCH[2]} 
		[[ "$(vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf -n 2> /dev/null | grep '\--memory')" =~ ^([^\"]*\"(.*)--memory.*)$ ]] || true
		RAM_SLOT=${BASH_REMATCH[2]} 
		[[ "$(vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf -n 2> /dev/null | grep '\--vmname')" =~ ^([^\"]*\"(.*)--vmname.*)$ ]] || true
		NAME_SLOT=${BASH_REMATCH[2]} 
		echo vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf $NAME_SLOT --vmname "${VM_NAME}" $CPU_SLOT --cpus 1 $RAM_SLOT --memory $VM_RAM_SIZE $DISK_SLOTS || true
		vboxmanage import ~/.vboxes/${GUEST_OS}/box.ovf $NAME_SLOT --vmname "${VM_NAME}" $CPU_SLOT --cpus 1 $RAM_SLOT --memory $VM_RAM_SIZE $DISK_SLOTS || true
		echo vboxmanage modifyvm ${VM_NAME} --nic2 hostonly
		vboxmanage modifyvm ${VM_NAME} --nic2 hostonly
		echo vboxmanage modifyvm ${VM_NAME} --hostonlyadapter2 ${VBOXNET}
		vboxmanage modifyvm ${VM_NAME} --hostonlyadapter2 ${VBOXNET}
		echo vboxmanage modifyvm ${VM_NAME} --cpuexecutioncap 80
		vboxmanage modifyvm ${VM_NAME} --cpuexecutioncap 80
		echo vboxmanage modifyvm ${VM_NAME} --hwvirtex on
		vboxmanage modifyvm ${VM_NAME} --hwvirtex on
		echo vboxmanage modifyvm ${VM_NAME} --natpf1 ,tcp,127.0.0.1,${HOST_SSH_PORT},,22
		vboxmanage modifyvm ${VM_NAME} --natpf1 ,tcp,127.0.0.1,${HOST_SSH_PORT},,22

		init ${ID} # will provision default
		if [ "x${PROVISION}" != "x" ]; then
			provision ${ID} ${PROVISION}
		fi
	} catch {
		echo -e "\e[31mError at line $ERR_LINENO code: $ERR_CODE Trace:\e[0m\n$ERR_TRACE";
		exit $ERR_CODE
	}
}

function provision () # ID PROVISION_NAME
{
	local ID=$1
	local PROVISION_NAME=$2
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/$VM_NAME.vbox

	try {
		VMNAME="\e[1m\e[32m${VM_NAME}\e[0m:"
		
		echo -e ${VMNAME} provisioning with ${PROVISION_NAME}
		generate vbox-pool/${PROVISION_NAME}.provision | vm_ssh 'cat > /vagrant/'${PROVISION_NAME}'.provision'
		# I can not use vm_ssh here. I need to be able to start
		# daemon(s) in provision whom does not close the standard
		# file descriptors.
		#sshpass -p vagrant /usr/bin/ssh -t -t \
		#	-o StrictHostKeyChecking=no \
		#	-o UserKnownHostsFile=/dev/null \
		#	-q -p ${HOST_SSH_PORT} vagrant@127.0.0.1 -- \
		#	sudo bash /vagrant/${PROVISION_NAME}.provision
		vm_ssh 'echo vagrant | sudo -S rm -f /vagrant/'${PROVISION_NAME}'.provision.done'
		vm_ssh 'echo vagrant | sudo -S bash /vagrant/'${PROVISION_NAME}'.provision'
		vm_ssh 'ls /vagrant/'${PROVISION_NAME}'.provision.done'
		echo -e ${VMNAME} provisioned with ${PROVISION_NAME}

		VM_TEMPLATE=$(echo $(printf '%s\n' ${VM_TEMPLATE} ${PROVISION_NAME} | sed -e "s|,||g" | sort -u))
		export_config ${ID}
	} catch {
		echo -e "\e[31mError at line $ERR_LINENO code: $ERR_CODE Trace:\e[0m\n$ERR_TRACE";
		exit $ERR_CODE
	}
}

function init () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/$VM_NAME.vbox

	try {
		VMNAME="\e[1m\e[32m${VM_NAME}\e[0m:"
		VM_SCP="sshpass -p vagrant scp -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -q -P ${HOST_SSH_PORT} "

		startvm ${ID}

		echo -e ${VMNAME} initializing
		generate vbox-pool/default.init | vm_ssh 'cat > default.init;'
		vm_ssh 'echo vagrant | sudo -S bash default.init'

		echo -e ${VMNAME} copy files
		${VM_SCP} vbox-pool/test_ssh_key vagrant@127.0.0.1:/vagrant/
		${VM_SCP} vbox-pool/test_ssh_key.pub vagrant@127.0.0.1:/vagrant/
		${VM_SCP} ~/.ssh/id_rsa.pub vagrant@127.0.0.1:/vagrant/tester_id_rsa.pub
		${VM_SCP} ~/.vimrc vagrant@127.0.0.1:/vagrant/
		${VM_SCP} ~/.vim/colors/*.vim vagrant@127.0.0.1:/vagrant/
		if [ -e ~/.vboxes/VBoxGuestAdditions_*.iso ]; then
			${VM_SCP} ~/.vboxes/VBoxGuestAdditions_*.iso vagrant@127.0.0.1:/vagrant/
		fi

		echo -e ${VMNAME} initialized
	} catch {
		echo -e "\e[31mError at line $ERR_LINENO code: $ERR_CODE Trace:\e[0m\n$ERR_TRACE";
		exit $ERR_CODE
	}

	try {
		provision ${ID} default
		# reboot and wait until ssh can not be (shut down) and then again can be used (start up)
		stopvm ${ID}
		startvm ${ID}
	} catch {
		echo -e "\e[31mError at line $ERR_LINENO code: $ERR_CODE Trace:\e[0m\n$ERR_TRACE";
		exit $ERR_CODE
	}
}

function destroy () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox

	if [ -e "vbox-pool/${VM_NAME}.vbox" ]; then
		if [ "$(vmstatus $ID)" = "running" ]; then
			vboxmanage controlvm ${VM_NAME} poweroff
		fi
		vboxmanage unregistervm ${VM_NAME} --delete && rm vbox-pool/${VM_NAME}.vbox
	fi
}

function startvm () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	if ! [ -e vbox-pool/${VM_NAME}.vbox ]; then
		echo $VM_NAME does not exist yet. You should create it.
		exit
	fi

	if [ "$(vmstatus ${ID})" = "running" ]; then
		echo $VM_NAME already running.
		return 0
	fi

	# boot and wait until ssh can be used
	vboxmanage startvm ${VM_NAME} --type headless

	echo -e ${VMNAME} waiting for it to boot and ssh daemon to work
	let I=0 || true
	while [ $I -lt 10 ]; do
		set +e
		#vm_ssh true 2> /dev/null
	    /usr/bin/ssh root@$VM_IP -- true 2> /dev/null
		RET=$?
		set -e
		if [ $RET = 0 ]; then
			echo -e ${VMNAME} vm started, ssh is available
			break;
		fi
		let I+=1 || true
		sleep 1
	done
}

function stopvm () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox

	# stop and wait until ssh can not be used
	#vboxmanage controlvm ${VM_NAME} acpipowerbutton
	vm_ssh 'echo vagrant | sudo -S shutdown -h now'
	while [ "$(vmstatus $ID)" != "off" ]; do
		echo -e ${VMNAME} Waiting for off state
		sleep 1
	done
}

function poweroff () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox
	if [ "$(vmstatus $ID)" = "running" ]; then
		vboxmanage controlvm ${VM_NAME} poweroff
	fi
}

function restartvm () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox
	vm_ssh 'echo vagrant | sudo -S shutdown -r now'
}

function save () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	vboxmanage controlvm ${VM_NAME} savestate
}

function snapshot () # ID SNAPSHOT_NAME
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	shift

	if [ "x$1" != "x" ]; then
		NAME=$1
	else
		NAME="default-snapshot"
	fi

	vboxmanage snapshot ${VM_NAME} take $NAME
}

function del_snapshot () # ID SNAPSHOT_NAME
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	shift

	if [ "x$1" != "x" ]; then
		NAME=$1
	else
		NAME="default-snapshot"
	fi

	vboxmanage snapshot ${VM_NAME} delete $NAME
}

function restore () # ID SNAPSHOT_NAME
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	shift

	if [ "x$1" != "x" ]; then
		NAME=$1
	else
		NAME="default-snapshot"
	fi

	if [ "$(vmstatus $ID)" = "running" ]; then
		vboxmanage controlvm ${VM_NAME} poweroff
	fi

	vboxmanage snapshot ${VM_NAME} restore $NAME
	startvm ${ID}
}

function motdfix () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	vm_ssh echo vagrant | sudo -S chmod a-x /etc/update-motd.d/* || true
}

function guest-additions () # ID HOST_DIR SF_NAME
{
	local ID=$1
	local HOST_DIR=$2
	local SF_NAME=$3
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/$VM_NAME.vbox

	vm_ssh echo vagrant | sudo -S apt-get -y install virtualbox-guest-dkms virtualbox-guest-utils virtualbox-guest-x11
	#VM_SCP="sshpass -p vagrant scp -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -q -P ${HOST_SSH_PORT} "
	#${VM_SCP} ~/.vboxes/VBoxGuestAdditions_4.3.34.iso vagrant@127.0.0.1:/vagrant/
	#echo vboxmanage guestcontrol ${VM_NAME} exec updateadditions --source /vagrant/VBoxGuestAdditions_4.3.34.iso --verbose --wait-start
	#vboxmanage guestcontrol ${VM_NAME} exec updateadditions --source /vagrant/VBoxGuestAdditions_4.3.34.iso --verbose --wait-start
	stopvm ${ID}
	#echo vboxmanage sharedfolder add ${VM_NAME} --name ${SF_NAME} --hostpath ${HOST_DIR} --automount
	vboxmanage sharedfolder add ${VM_NAME} --name ${SF_NAME} --hostpath ${HOST_DIR} --automount
}

function ssh () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox

	/usr/bin/ssh root@$VM_IP
}

function vssh () # ID
{
	local ID=$1
	VM_NAME=$(vm_name ${ID})
	source vbox-pool/${VM_NAME}.vbox

	sshpass -p vagrant /usr/bin/ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -q -p ${HOST_SSH_PORT} vagrant@127.0.0.1
}

function boxlist ()
{
	LIST=""; for I in "${!IMAGES_URL[@]}"; do LIST=$LIST" "$I; done
	for I in $(echo $LIST | tr ' ' '\n' | sort); do
		echo -e $I
	done
}

function list ()
{
	printf "%-30s%-16s%-20s%-10s%-30s%s\n" "Name" "OS/Box image" "Ip" "Status" "Used template" "Snapshots"
	printf "%-30s%-16s%-20s%-10s%-30s%s\n" "----" "------------" "--" "------" "-------------" "---------"
	for VM_NAME in $(ls -1 vbox-pool/${VM_NAME_PREFIX}-*.vbox 2> /dev/null); do
		VM_NAME=$(basename $VM_NAME | cut -d . -f 1)
		source vbox-pool/${VM_NAME}.vbox
		vboxmanage showvminfo ${VM_NAME} > $VM_INFO_LOG
		STATUSLINE=$(cat $VM_INFO_LOG | grep "^State:")
		SNAPSHOT_LINE_POS=$(cat $VM_INFO_LOG | grep -n "^Snapshots:" | cut -d : -f 1 -)
		if [ x$SNAPSHOT_LINE_POS != x ]; then
			TOTAL_INFO_LINES=$(cat $VM_INFO_LOG | wc -l)
			let SNAPSHOT_LINES=$TOTAL_INFO_LINES-$SNAPSHOT_LINE_POS
			SNAPSHOT_LIST=$(cat $VM_INFO_LOG | \
					tail -n $SNAPSHOT_LINES | \
					grep "Name:" | \
					grep -oP 'Name: \K[^ ]*' | tr '\n' ' ')
		else
			SNAPSHOT_LIST=""
		fi
		rm $VM_INFO_LOG 2> /dev/null
		if [[ "$STATUSLINE" =~ ^(.*[ ]+([^ ]+) \(.*)$ ]]; then
			STATUS=${BASH_REMATCH[2]} 
		else
			STATUS=unknown
		fi

		printf "%-30s%-16s%-20s%-10s%-30s%s\n" "$VM_NAME" "$GUEST_OS" "$VM_IP" "$STATUS" "$VM_TEMPLATE" "$SNAPSHOT_LIST"
	done
}

#
#	Parameter reading
#

precache_images		# without precached images we do not do anything

NODE_ID_LIST=""

while true; do
	# range of VMs to operate on
	if [[ "$1" =~ ^([0-9]+)\.\.([0-9]+)$ ]]; then
		FIRST_ID=${BASH_REMATCH[1]} 
		LAST_ID=${BASH_REMATCH[2]}
		shift
		for((ID = ${FIRST_ID}; ID <= ${LAST_ID}; ID++)) do
			NODE_ID_LIST=${NODE_ID_LIST}" "${ID}
		done
	elif [[ "$1" =~ ^([0-9]+)-([0-9]+)$ ]]; then
		FIRST_ID=${BASH_REMATCH[1]} 
		LAST_ID=${BASH_REMATCH[2]}
		for((ID = ${FIRST_ID}; ID <= ${LAST_ID}; ID++)) do
			NODE_ID_LIST=${NODE_ID_LIST}" "${ID}
		done
		shift
	elif [[ "$1" =~ ^([0-9]+)$ ]]; then
		FIRST_ID=${BASH_REMATCH[1]}
		LAST_ID=${BASH_REMATCH[1]}
		for((ID = ${FIRST_ID}; ID <= ${LAST_ID}; ID++)) do
			NODE_ID_LIST=${NODE_ID_LIST}" "${ID}
		done
		shift
	else
		break
	fi
done


VM_INFO_LOG="/dev/shm/vminfo.${BASHPID}.trap.log"
trap 'rm \$VM_INFO_LOG 2> /dev/null || true' EXIT

# command
CMD=$1
shift
case "${CMD}" in
	(boxlist)
		boxlist
		;;
	(list)
		list
		;;
	(ls)
		list
		;;
	(vssh)
		vssh $@
		;;
	(ssh)
		ssh $@
		;;
	(*)
		if [ "${NODE_ID_LIST}" = "" ]; then
			echo "Missing id list"
			exit 1
		fi
		# parallel execution of the requested command
		#PIDS=()
        unset PIDS
		declare -A PIDS
		for ID in ${NODE_ID_LIST}; do
			echo $CMD on $(vm_name ${ID})

			(${CMD} ${ID} $@ 2>&1 | tee ./vbox-pool/$(vm_name ${ID}).log) &
			PIDS[${ID}]=$!
		done

		wait_for_workers
        exit $?
		;;
esac

