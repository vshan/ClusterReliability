// Write your JavaScript code.
var app = angular.module('CBA', ['ngRoute','ui.bootstrap']);
app.run(function ($rootScope, $location) {
    $rootScope.$on("$locationChangeStart", function (event, next, current) {

        /*
        if ((($location.path() === "/") || ($location.path() === "")) && $rootScope.appsConfigured.length === 0) {
            $rootScope.showConfiguredApps = false;

        }
        */
        
    });
    $rootScope.selectedApps = [];
});

app.config(function ($routeProvider) {
    $routeProvider

        .when('/', {
            templateUrl: 'Status',
            controller: 'CBAController'
        })

        .when('/Configure', {
            templateUrl: 'Applications',
            controller: 'CBAController'
        })

        .otherwise({ redirectTo: '/' });
});

app.controller('CBAController', ['$rootScope', '$scope', '$http', '$timeout', '$location','$uibModal', function ($rootScope, $scope, $http, $timeout, $location, $uibModal) {

    $scope.showForm = false;
    $scope.policy = {};

    $scope.openModal = function (app) {
        $scope.appToConfigure = app;
        $scope.modalInstance = $uibModal.open({
            templateUrl: 'Modal',
            scope: $scope
        });
    };

    var loadTime = 10000, //Load the data every second
        errorCount = 0, //Counter for the server errors
        loadPromise; //Pointer to the promise created by the Angular $timout service

    $scope.refresh = function () {
        console.log("Hey");
        $http.get('api/RestoreService/status')
            .then(function (data, status) {
                $rootScope.partitionsStatus = data.data;
                if ($rootScope.partitionsStatus == undefined) {
                    $rootScope.showConfiguredApps = false;
                    return;
                }
                var appsConfigured = [];
                console.log($rootScope.partitionsStatus[0].applicationName);
                for (var i in $rootScope.partitionsStatus) {
                    var appName = $rootScope.partitionsStatus[i].applicationName;
                    if (appsConfigured.indexOf(appName) == -1)
                        appsConfigured.push(appName);
                }
                $rootScope.appsConfigured = appsConfigured;
                if ($rootScope.appsConfigured.length > 0)
                    $rootScope.showConfiguredApps = true;
                else 
                    $rootScope.showConfiguredApps = false;
                errorCount = 0;
                nextLoad();

            }, function (data, status) {
                console.log("Not happening");
                nextLoad(++errorCount * 2 * loadTime);
            });
    };

    var cancelNextLoad = function () {
        $timeout.cancel(loadPromise);
    };

    var nextLoad = function (mill) {
        mill = mill || loadTime;

        //Always make sure the last timeout is cleared before starting a new one
        cancelNextLoad();
        loadPromise = $timeout($scope.refresh, mill);
    };

    //Always clear the timeout when the view is destroyed, otherwise it will keep polling and leak memory
    $scope.$on('$destroy', function () {
        cancelNextLoad();
    });

    $scope.configure = function () {
/*        if ($rootScope.appsConfigured.indexOf($scope.appToConfigure) !== -1) {
            window.alert("Application already configured");
            $scope.modalInstance.dismiss('cancel');
            return;
        }
        */
        console.log("In Configure : " + $scope.policies[0].backupStorage.storageKind);
        console.log("In Configure : " + $scope.policies[0].backupStorage.primaryUsername);
        console.log("In Configure : " + $rootScope.showConfiguredApps);
        var contentData = {};
        contentData.PoliciesList = $scope.policies;
        contentData.ApplicationsList = $rootScope.selectedApps;
        var content = JSON.stringify(contentData);
        $http.post('api/RestoreService/configure/' + $rootScope.pc + '/' + $rootScope.sc + '/' + $rootScope.php + '/' + $rootScope.shp, content)
            .then(function (data, status) {
                console.log("Succesfully configured");
                window.alert("Applications Successfully configured");
            }, function (data, status) {
                window.alert("Applications not configured. Try again");
                console.log("Not configured");
            });
        $scope.cancel(true);
    };

    $scope.cancel = function (modalInstance) {
        if (modalInstance === 'configureModalInstance')
            $scope.configureModalInstance.dismiss();
        else if (modalInstance === 'policyModalInstance')
            $scope.policyModalInstance.dismiss();
        else if (modalInstance === 'statusModalInstance')
            $scope.statusModalInstance.dismiss();
        else {
            $scope.configureModalInstance.dismiss();
            $scope.policyModalInstance.dismiss();
        }
    };

    $scope.status = {
    isFirstOpen: true,
    isFirstDisabled: false
    };

    $scope.openStatusModal = function (configuredApp) {
        $scope.configuredApp = configuredApp;
        $scope.applicationStatus = [];
        for (var i in $rootScope.partitionsStatus) {
            if ($scope.partitionsStatus[i].applicationName.includes(configuredApp)) {
                $scope.applicationStatus.push($rootScope.partitionsStatus[i]);
            }
        }
        $scope.statusModalInstance = $uibModal.open({
            templateUrl: 'StatusModal',
            scope: $scope,
            windowClass: 'app-modal-window'
        });
    };

    $scope.disconfigure = function (configuredApp) {
        if (configuredApp.includes("fabric:/"))
            configuredApp = configuredApp.replace('fabric:/', '');
        $http.get('api/RestoreService/disconfigure/' + configuredApp)
            .then(function (data, status) {
                if (data.data == configuredApp)
                    window.alert("Suuccessfully disconfigured");
                $scope.cancel('statusModalInstance');
            }, function (data, status) {
                window.alert("Problem while disconfiguring");
            });
    }

    $scope.toggleSelection = function (app) {
        var idx = $rootScope.selectedApps.indexOf(app);

        // Is currently selected
        if (idx > -1) {
            $rootScope.selectedApps.splice(idx, 1);
        }

        // Is newly selected
        else {
            $rootScope.selectedApps.push(app);
        }
    };

    $scope.getapps = function () {
//        var cs = $scope.getQueryParameterByName('cs');
        $rootScope.pc = $scope.pc;
        $rootScope.sc = $scope.sc;
        $rootScope.php = $scope.php;
        $rootScope.shp = $scope.shp;
        var address = $scope.pc;
        if (address.includes("http://"))
            address = address.replace("http://", "");
        if (address.includes("https://"))
            address = address.replace("https://", "");
        $scope.pc = $rootScope.pc = address;
        $http.get('api/RestoreService/' + $scope.pc + '/' + $scope.php)
            .then(function (data, status) {
                $scope.apps = data;
                $scope.configureModalInstance = $uibModal.open({
                    templateUrl: 'ConfigureModal',
                    scope: $scope,
                    windowClass: 'app--window'
                });
                console.log("Done reading");
                console.log(data);
                console.log($scope.apps.data[0]);
            }, function (data, status) {
                $scope.apps = undefined;
            });
    };

    $scope.displayForm = function () {
        $scope.showForm = true;
    };

    $scope.getpolicies = function () {
//        var cs = $scope.getQueryParameterByName('cs');
        $http.get('api/RestoreService/policies/' + $scope.pc + ':' + $scope.hp)
            .then(function (data, status) {
                $scope.policies = data;
                console.log("Policies read");
                console.log($scope.policies.data[0]);
            }, function (data, status) {
                console.log("Sad life");
                $scope.policies = undefined;
            });
    };

    $scope.openPolicyModal = function () {
        $http.post('api/RestoreService/policies/' + $scope.pc + ':' + $scope.php, $rootScope.selectedApps)
            .then(function (data, status) {
                $scope.policies = data.data;
                console.log($scope.policies[0].backupStorage.primaryUsername);
                $scope.policyModalInstance = $uibModal.open({
                    templateUrl: 'PolicyModal',
                    scope: $scope
                });
            }, function (data, status) {
                console.log("Sad life");
                $scope.policies = undefined;
            });
        /*
        $uibModal.open({
            templateUrl: 'PolicyModal',
            controller: function ($scope, $rootScope, $uibModalInstance) {
                $scope.policy = {};
                $scope.submitstoragedetails = function () {
                    var content = JSON.stringify($scope.policy);
                    $http.post('api/RestoreService/add/' + policy + '/' + $rootScope.pc + ':' + $rootScope.hp, content)
                        .then(function (data, status) {
                            console.log("Submitted");
                            window.alert("Storage Deatils succesfully submitted");
                        }, function (data, status) {
                            //$scope.apps = undefined;
                        });
                    $uibModalInstance.close();
                };

                $scope.cancel = function () {
                    $uibModalInstance.dismiss('cancel');
                };
            }
        });
        */
    };

    /*
    $scope.submit = function () {
        $rootScope.pc = $scope.pc;
        $rootScope.sc = $scope.sc;
        $rootScope.tp = $scope.tp;
        $rootScope.hp = $scope.hp;
        console.log('Sc is : ' + $rootScope.sc);
        var path = '/Policies';
        $location.path(path);
    };
    */


    $scope.redirect = function () {
        var path = '/About?cs=';
        var address = path.concat($scope.pc + ':' + $scope.hp);
        window.location.href = address;
    };

    $scope.remove = function (item) {
        $http.delete('api/Votes/' + item)
            .then(function (data, status) {
                $scope.refresh();
            });
    };

    $scope.getQueryParameterByName = function (name) {

        name = name.replace(/[\[]/, "\\[").replace(/[\]]/, "\\]");

        var regex = new RegExp("[\\?&]" + name + "=([^&#]*)"), results = regex.exec(location.search);

        return results === null ? "" : decodeURIComponent(results[1].replace(/\+/g, " "));
    };

    $scope.send = function (cs) {
        /*
        var fd = new FormData();
        fd.append('item', item);
        $http.put('api/Votes/' + item, fd, {
            transformRequest: angular.identity,
            headers: { 'Content-Type': undefined }
        })
            .then(function (data, status) {
                $scope.refresh();
                $scope.item = undefined;
            })
    };*/
        console.log("You entered : " + cs);
    };
}]);