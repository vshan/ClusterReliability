// Write your JavaScript code.
var app = angular.module('StandByApplication', ['ngRoute', 'ui.bootstrap']);

app.run(function ($rootScope, $location) {
    $rootScope.selectedApps = [];
});

    // Routing to specific pages given the templates
app.config(function ($routeProvider) {
    $routeProvider

        .when('/', {
            templateUrl: 'Status',
            controller: 'StandByApplicationController'
        })

        .when('/Configure', {
            templateUrl: 'Applications',
            controller: 'StandByApplicationController'
        })

        .otherwise({ redirectTo: '/' });
});

app.controller('StandByApplicationController', ['$rootScope', '$scope', '$http', '$timeout', '$location','$uibModal', function ($rootScope, $scope, $http, $timeout, $location, $uibModal) {

    var loadTime = 10000, //Load the data every second
        errorCount = 0, //Counter for the server errors
        loadPromise; //Pointer to the promise created by the Angular $timout service


    // This will be called whenever the page is refreshed
    $scope.refresh = function () {
        $http.get('api/RestoreService/status')
            .then(function (data, status) {
                $rootScope.partitionsStatus = data.data;
                if ($rootScope.partitionsStatus == undefined) {
                    $rootScope.showConfiguredApps = false;
                    return;
                }

                var appsConfigured = [];

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
                nextLoad();     //Calls the next load

            }, function (data, status) {
                nextLoad(++errorCount * 2 * loadTime);   // If current request fails next load will be delayed
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

        var contentData = {};
        contentData.PoliciesList = $scope.policies;
        contentData.ApplicationsList = $rootScope.selectedApps;

        var content = JSON.stringify(contentData);

        $http.post('api/RestoreService/configure/' + $rootScope.pc + '/' + $rootScope.sc + '/' + $rootScope.php + '/' + $rootScope.shp, content)
            .then(function (data, status) {
                window.alert("Applications Successfully configured");
            }, function (data, status) {
                window.alert("Applications not configured. Try again");
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

        $rootScope.pc = $scope.pc;
        $rootScope.sc = $scope.sc;
        $rootScope.php = $scope.php;
        $rootScope.shp = $scope.shp;

        var primaryAddress = $scope.pc;

        if (primaryAddress.includes("http://"))
            primaryAddress = primaryAddress.replace("http://", "");

        if (primaryAddress.includes("https://"))
            primaryAddress = primaryAddress.replace("https://", "");

        $scope.pc = $rootScope.pc = primaryAddress;

        var secondaryAddress = $scope.sc;

        if (secondaryAddress.includes("http://"))
            secondaryAddress = secondaryAddress.replace("http://", "");

        if (secondaryAddress.includes("https://"))
            secondaryAddress = secondaryAddress.replace("https://", "");

        $scope.sc = $rootScope.sc = secondaryAddress;

        $http.get('api/RestoreService/' + $scope.pc + '/' + $scope.php)
            .then(function (data, status) {
                $scope.apps = data;
                $scope.configureModalInstance = $uibModal.open({
                    templateUrl: 'ConfigureModal',
                    scope: $scope,
                    windowClass: 'app--window'
                });
            }, function (data, status) {
                $scope.apps = undefined;
                window.alert('Please check the cluster details and try again');
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
                $scope.policies = undefined;
            });
    };
}]);